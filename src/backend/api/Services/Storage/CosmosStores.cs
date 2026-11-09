using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Chatbot.Api.Services.Storage;

/// <summary>
/// Stores chats and messages in one container with hierarchical partition key [/userId, /chatId]:
/// listing a user's chats is a prefix query, loading a chat is a single-partition query.
/// </summary>
public sealed class CosmosChatStore(CosmosClient client, IOptions<CosmosOptions> options, TimeProvider time) : IChatStore
{
    private const int SchemaVersion = 2;
    private readonly Container _container = client.GetContainer(options.Value.DatabaseName, options.Value.ChatsContainer);

    public async Task<IReadOnlyList<ChatSummary>> ListAsync(string userId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
            "SELECT c.id, c.title, c.createdAt, c.updatedAt, c.documentIds FROM c WHERE c.type = 'chat' ORDER BY c.updatedAt DESC");
        return await ReadAllAsync<ChatSummary>(query, new PartitionKeyBuilder().Add(userId).Build(), cancellationToken);
    }

    public async Task<ChatSummary?> GetAsync(string userId, string chatId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _container.ReadItemAsync<ChatItem>(chatId, Key(userId, chatId), cancellationToken: cancellationToken);
            return new ChatSummary(response.Resource.Id, response.Resource.Title, response.Resource.CreatedAt, response.Resource.UpdatedAt)
            {
                DocumentIds = response.Resource.DocumentIds ?? []
            };
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string userId, string chatId, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
            "SELECT c.id, c.role, c.content, c.createdAt, c.citations, c.retrieval, c.skill, c.followUpQuestions FROM c WHERE c.type = 'message' ORDER BY c.createdAt ASC");
        return await ReadAllAsync<ChatMessage>(query, Key(userId, chatId), cancellationToken);
    }

    public async Task<ChatSummary> CreateAsync(string userId, string title, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var chatId = Guid.NewGuid().ToString();
        var item = new ChatItem(chatId, "chat", userId, chatId, title, now, now, SchemaVersion, []);
        await _container.CreateItemAsync(item, Key(userId, chatId), cancellationToken: cancellationToken);
        return new ChatSummary(chatId, title, now, now);
    }

    public async Task AddMessageAsync(string userId, string chatId, ChatMessage message, CancellationToken cancellationToken)
    {
        var item = new MessageItem(message.Id, "message", userId, chatId, message.Role, message.Content, message.CreatedAt,
            SchemaVersion, message.Citations, message.Retrieval, message.Skill, message.FollowUpQuestions);
        using var response = await _container.CreateTransactionalBatch(Key(userId, chatId))
            .CreateItem(item)
            .PatchItem(chatId, [PatchOperation.Set("/updatedAt", message.CreatedAt)])
            .ExecuteAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Saving the chat message failed with status {(int)response.StatusCode}.");
        }
    }

    public async Task<bool> DeleteAsync(string userId, string chatId, CancellationToken cancellationToken)
    {
        var key = Key(userId, chatId);
        var ids = await ReadAllAsync<string>(new QueryDefinition("SELECT VALUE c.id FROM c"), key, cancellationToken);
        if (ids.Count == 0)
        {
            return false;
        }

        foreach (var batchIds in ids.Chunk(100))
        {
            var batch = _container.CreateTransactionalBatch(key);
            foreach (var id in batchIds)
            {
                batch.DeleteItem(id);
            }

            using var response = await batch.ExecuteAsync(cancellationToken);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                throw new InvalidOperationException($"Deleting the chat failed with status {(int)response.StatusCode}.");
            }
        }

        return true;
    }

    private static PartitionKey Key(string userId, string chatId) => new PartitionKeyBuilder().Add(userId).Add(chatId).Build();

    public Task SetDocumentsAsync(string userId, string chatId, IReadOnlyList<string> documentIds, CancellationToken cancellationToken) =>
        _container.PatchItemAsync<ChatItem>(chatId, Key(userId, chatId),
            [PatchOperation.Set("/documentIds", documentIds), PatchOperation.Set("/updatedAt", time.GetUtcNow())],
            cancellationToken: cancellationToken);

    private async Task<List<T>> ReadAllAsync<T>(QueryDefinition query, PartitionKey key, CancellationToken cancellationToken)
    {
        var results = new List<T>();
        using var iterator = _container.GetItemQueryIterator<T>(query, requestOptions: new QueryRequestOptions { PartitionKey = key });
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return results;
    }

    private sealed record ChatItem(string Id, string Type, string UserId, string ChatId, string Title,
        DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int SchemaVersion, IReadOnlyList<string>? DocumentIds);

    private sealed record MessageItem(string Id, string Type, string UserId, string ChatId, string Role, string Content,
        DateTimeOffset CreatedAt, int SchemaVersion, IReadOnlyList<PassageCitation> Citations, RetrievalDetails? Retrieval, string? Skill,
        IReadOnlyList<string> FollowUpQuestions);
}

/// <summary>Registry of ingested user documents, partitioned by /userId.</summary>
public sealed class CosmosDocumentRegistry(CosmosClient client, IOptions<CosmosOptions> options) : IDocumentRegistry
{
    private readonly Container _container = client.GetContainer(options.Value.DatabaseName, options.Value.DocumentsContainer);

    public async Task<IReadOnlyList<UserDocument>> ListAsync(string userId, CancellationToken cancellationToken)
    {
        var results = new List<UserDocument>();
        using var iterator = _container.GetItemQueryIterator<UserDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.status != 'deleted' ORDER BY c.createdAt DESC"),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(userId) });
        while (iterator.HasMoreResults)
        {
            results.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return results;
    }

    public async Task<UserDocument?> GetAsync(string userId, string documentId, CancellationToken cancellationToken)
    {
        try
        {
            var document = (await _container.ReadItemAsync<UserDocument>(documentId, new PartitionKey(userId),
                cancellationToken: cancellationToken)).Resource;
            return document.Status == DocumentStatus.Deleted ? null : document;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task CreateAsync(UserDocument document, CancellationToken cancellationToken) =>
        _container.CreateItemAsync(document, new PartitionKey(document.UserId), cancellationToken: cancellationToken);

    public async Task<bool> TryCompleteAsync(UserDocument result, CancellationToken cancellationToken) =>
        await UpdateAsync(result.UserId, result.Id, current => DocumentTransitions.Complete(current, result), cancellationToken)
            is { Status: DocumentStatus.Ready or DocumentStatus.Failed };

    public Task<UserDocument?> MarkDeletedAsync(string userId, string documentId, CancellationToken cancellationToken) =>
        UpdateAsync(userId, documentId, DocumentTransitions.Delete, cancellationToken);

    public async Task<UserDocument?> GetDeletedAsync(string userId, string documentId, CancellationToken cancellationToken)
    {
        try
        {
            var document = (await _container.ReadItemAsync<UserDocument>(documentId, new PartitionKey(userId),
                cancellationToken: cancellationToken)).Resource;
            return document.Status == DocumentStatus.Deleted ? document : null;
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> CompleteCleanupAsync(string userId, string documentId, CancellationToken cancellationToken) =>
        await UpdateAsync(userId, documentId, DocumentTransitions.Clean, cancellationToken) is not null;

    public async Task<DocumentCleanupPage> ReadCleanupPageAsync(string? continuation, CancellationToken cancellationToken)
    {
        using var iterator = _container.GetItemQueryIterator<UserDocument>(
            new QueryDefinition("SELECT * FROM c WHERE c.status = 'deleted' AND c.cleanupPending = true"),
            continuation, new QueryRequestOptions { MaxItemCount = 50 });
        var page = await iterator.ReadNextAsync(cancellationToken);
        return new(page.ToArray(), page.ContinuationToken);
    }

    private async Task<UserDocument?> UpdateAsync(string userId, string documentId, Func<UserDocument, UserDocument?> transition,
        CancellationToken cancellationToken)
    {
        var key = new PartitionKey(userId);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var response = await _container.ReadItemAsync<UserDocument>(documentId, key, cancellationToken: cancellationToken);
                var updated = transition(response.Resource);
                if (updated is null) return null;
                await _container.ReplaceItemAsync(updated, documentId, key,
                    new ItemRequestOptions { IfMatchEtag = response.ETag }, cancellationToken);
                return updated;
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed && attempt < 4)
            {
                // Re-read after a concurrent completion or deletion; never upsert.
            }
        }
        throw new InvalidOperationException("The document lifecycle update did not complete.");
    }
}
