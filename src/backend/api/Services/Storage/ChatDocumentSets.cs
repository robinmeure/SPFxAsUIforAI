namespace Chatbot.Api.Services.Storage;

public sealed record DocumentSetConflict(string ChatId, string Title);

public abstract record DocumentSetResult
{
    public sealed record Updated(IReadOnlyList<string> DocumentIds, ChatMessage? Event) : DocumentSetResult;
    public sealed record Invalid(string Detail) : DocumentSetResult;
    public sealed record Duplicate(DocumentSetConflict Conflict) : DocumentSetResult;
}

/// <summary>
/// A conversation owns exactly one document set and no two conversations of a user share the same non-empty set.
/// An empty set is a general chat and is exempt. Changing the set of a conversation with messages records an event
/// message, so the thread shows that later answers were searched in different documents.
/// </summary>
public sealed class ChatDocumentSets(IChatStore chats, IDocumentRegistry documents, TimeProvider time)
{
    public const int MaxDocuments = 20;
    public const string EventRole = "event";

    public static bool SameSet(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right) =>
        left.Count == right.Count && left.ToHashSet(StringComparer.Ordinal).SetEquals(right);

    /// <summary>Validates ownership and uniqueness without writing; <paramref name="chatId"/> is excluded from the duplicate check.</summary>
    public async Task<DocumentSetResult> CheckAsync(string userId, string? chatId, IReadOnlyList<string> requested,
        CancellationToken cancellationToken)
    {
        var ids = requested.Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length > MaxDocuments)
        {
            return new DocumentSetResult.Invalid($"A conversation can use at most {MaxDocuments} documents.");
        }

        if (ids.Length > 0)
        {
            var owned = (await documents.ListAsync(userId, cancellationToken)).Select(document => document.Id).ToHashSet(StringComparer.Ordinal);
            var existing = chatId is null ? null : await chats.GetAsync(userId, chatId, cancellationToken);
            if (ids.Any(id => !owned.Contains(id) && !(existing?.DocumentIds.Contains(id) ?? false)))
            {
                return new DocumentSetResult.Invalid("One or more documents are unavailable or not owned by you.");
            }

            var conflict = (await chats.ListAsync(userId, cancellationToken))
                .FirstOrDefault(chat => chat.Id != chatId && SameSet(chat.DocumentIds, ids));
            if (conflict is not null)
            {
                return new DocumentSetResult.Duplicate(new(conflict.Id, conflict.Title));
            }
        }

        return new DocumentSetResult.Updated(ids, null);
    }

    public async Task<DocumentSetResult> ApplyAsync(string userId, ChatSummary chat, IReadOnlyList<string> requested,
        CancellationToken cancellationToken)
    {
        var check = await CheckAsync(userId, chat.Id, requested, cancellationToken);
        if (check is not DocumentSetResult.Updated { DocumentIds: var ids })
        {
            return check;
        }

        if (SameSet(chat.DocumentIds, ids))
        {
            return check;
        }

        await chats.SetDocumentsAsync(userId, chat.Id, ids, cancellationToken);
        if ((await chats.GetMessagesAsync(userId, chat.Id, cancellationToken)).Count == 0)
        {
            return check;
        }

        var names = (await documents.ListAsync(userId, cancellationToken)).ToDictionary(document => document.Id, document => document.FileName);
        string Describe(IEnumerable<string> changed) =>
            string.Join(", ", changed.Select(id => names.TryGetValue(id, out var name) ? name : "an unavailable document"));
        var added = ids.Except(chat.DocumentIds, StringComparer.Ordinal).ToArray();
        var removed = chat.DocumentIds.Except(ids, StringComparer.Ordinal).ToArray();
        var parts = new List<string>();
        if (added.Length > 0) parts.Add($"Added {Describe(added)}");
        if (removed.Length > 0) parts.Add($"Removed {Describe(removed)}");
        var change = new ChatMessage(Guid.NewGuid().ToString(), EventRole, string.Join(" · ", parts), time.GetUtcNow());
        await chats.AddMessageAsync(userId, chat.Id, change, cancellationToken);
        return new DocumentSetResult.Updated(ids, change);
    }
}
