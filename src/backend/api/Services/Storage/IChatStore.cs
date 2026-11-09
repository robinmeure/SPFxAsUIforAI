namespace Chatbot.Api.Services.Storage;

public interface IChatStore
{
    Task<IReadOnlyList<ChatSummary>> ListAsync(string userId, CancellationToken cancellationToken);
    Task<ChatSummary?> GetAsync(string userId, string chatId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string userId, string chatId, CancellationToken cancellationToken);
    Task<ChatSummary> CreateAsync(string userId, string title, CancellationToken cancellationToken);
    Task AddMessageAsync(string userId, string chatId, ChatMessage message, CancellationToken cancellationToken);
    Task SetDocumentsAsync(string userId, string chatId, IReadOnlyList<string> documentIds, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string userId, string chatId, CancellationToken cancellationToken);
}

public interface IDocumentRegistry
{
    Task<IReadOnlyList<UserDocument>> ListAsync(string userId, CancellationToken cancellationToken);
    Task<UserDocument?> GetAsync(string userId, string documentId, CancellationToken cancellationToken);
    Task CreateAsync(UserDocument document, CancellationToken cancellationToken);
    /// <summary>Completes processing atomically, or acknowledges a deleted job without making it visible again.</summary>
    Task<bool> TryCompleteAsync(UserDocument result, CancellationToken cancellationToken);
    Task<UserDocument?> MarkDeletedAsync(string userId, string documentId, CancellationToken cancellationToken);
    Task<UserDocument?> GetDeletedAsync(string userId, string documentId, CancellationToken cancellationToken);
    Task<bool> CompleteCleanupAsync(string userId, string documentId, CancellationToken cancellationToken);
    Task<DocumentCleanupPage> ReadCleanupPageAsync(string? continuation, CancellationToken cancellationToken);
}
