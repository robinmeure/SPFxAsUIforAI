using System.Collections.Concurrent;

namespace Chatbot.Api.Services.Storage;

/// <summary>Process-local store for local development and tests when Cosmos DB is not configured.</summary>
public sealed class InMemoryChatStore(TimeProvider time) : IChatStore
{
    private readonly ConcurrentDictionary<(string User, string Chat), ChatSummary> _chats = new();
    private readonly ConcurrentDictionary<(string User, string Chat), ConcurrentQueue<ChatMessage>> _messages = new();

    public Task<IReadOnlyList<ChatSummary>> ListAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ChatSummary>>(_chats.Where(entry => entry.Key.User == userId)
            .Select(entry => entry.Value).OrderByDescending(chat => chat.UpdatedAt).ToList());

    public Task<ChatSummary?> GetAsync(string userId, string chatId, CancellationToken cancellationToken) =>
        Task.FromResult(_chats.TryGetValue((userId, chatId), out var chat) ? chat : null);

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string userId, string chatId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ChatMessage>>(
            _messages.TryGetValue((userId, chatId), out var messages) ? messages.ToList() : []);

    public Task<ChatSummary> CreateAsync(string userId, string title, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var chat = new ChatSummary(Guid.NewGuid().ToString(), title, now, now);
        _chats[(userId, chat.Id)] = chat;
        _messages[(userId, chat.Id)] = new();
        return Task.FromResult(chat);
    }

    public Task AddMessageAsync(string userId, string chatId, ChatMessage message, CancellationToken cancellationToken)
    {
        if (_messages.TryGetValue((userId, chatId), out var messages))
        {
            messages.Enqueue(message);
            _chats.AddOrUpdate((userId, chatId), _ => throw new InvalidOperationException("Chat was deleted."),
                (_, chat) => chat with { UpdatedAt = message.CreatedAt });
        }

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(string userId, string chatId, CancellationToken cancellationToken)
    {
        _messages.TryRemove((userId, chatId), out _);
        return Task.FromResult(_chats.TryRemove((userId, chatId), out _));
    }

    public Task SetDocumentsAsync(string userId, string chatId, IReadOnlyList<string> documentIds, CancellationToken cancellationToken)
    {
        _chats.AddOrUpdate((userId, chatId), _ => throw new InvalidOperationException("Chat was deleted."),
            (_, chat) => chat with { DocumentIds = documentIds.ToArray(), UpdatedAt = time.GetUtcNow() });
        return Task.CompletedTask;
    }
}

public sealed class InMemoryDocumentRegistry : IDocumentRegistry
{
    private readonly ConcurrentDictionary<(string User, string Id), UserDocument> _documents = new();

    public Task<IReadOnlyList<UserDocument>> ListAsync(string userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserDocument>>(_documents.Values.Where(document => document.UserId == userId && document.Status != DocumentStatus.Deleted)
            .OrderByDescending(document => document.CreatedAt).ToList());

    public Task<UserDocument?> GetAsync(string userId, string documentId, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.TryGetValue((userId, documentId), out var document) && document.Status != DocumentStatus.Deleted ? document : null);

    public Task CreateAsync(UserDocument document, CancellationToken cancellationToken)
    {
        if (!_documents.TryAdd((document.UserId, document.Id), document))
            throw new InvalidOperationException("The document already exists.");
        return Task.CompletedTask;
    }

    public async Task<bool> TryCompleteAsync(UserDocument result, CancellationToken cancellationToken) =>
        await UpdateAsync(result.UserId, result.Id, current => DocumentTransitions.Complete(current, result), cancellationToken)
            is { Status: DocumentStatus.Ready or DocumentStatus.Failed };

    public Task<UserDocument?> MarkDeletedAsync(string userId, string documentId, CancellationToken cancellationToken) =>
        UpdateAsync(userId, documentId, DocumentTransitions.Delete, cancellationToken);

    public Task<UserDocument?> GetDeletedAsync(string userId, string documentId, CancellationToken cancellationToken) =>
        Task.FromResult(_documents.TryGetValue((userId, documentId), out var document) && document.Status == DocumentStatus.Deleted ? document : null);

    public async Task<bool> CompleteCleanupAsync(string userId, string documentId, CancellationToken cancellationToken) =>
        await UpdateAsync(userId, documentId, DocumentTransitions.Clean, cancellationToken) is not null;

    public Task<DocumentCleanupPage> ReadCleanupPageAsync(string? continuation, CancellationToken cancellationToken)
    {
        var documents = _documents.Values.Where(document => document.CleanupPending)
            .OrderBy(document => document.UserId + "/" + document.Id, StringComparer.Ordinal)
            .Where(document => continuation is null || string.CompareOrdinal(document.UserId + "/" + document.Id, continuation) > 0)
            .Take(50).ToArray();
        return Task.FromResult(new DocumentCleanupPage(documents,
            documents.Length == 50 ? documents[^1].UserId + "/" + documents[^1].Id : null));
    }

    private Task<UserDocument?> UpdateAsync(string userId, string documentId, Func<UserDocument, UserDocument?> transition,
        CancellationToken cancellationToken)
    {
        while (_documents.TryGetValue((userId, documentId), out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var updated = transition(current);
            if (updated is null) break;
            if (_documents.TryUpdate((userId, documentId), updated, current))
                return Task.FromResult<UserDocument?>(updated);
        }
        return Task.FromResult<UserDocument?>(null);
    }
}
