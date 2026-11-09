using System.Text.Json.Serialization;

namespace Chatbot.Api.Services.Storage;

public sealed record ChatSummary(string Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public IReadOnlyList<string> DocumentIds { get; init; } = [];
}

public sealed record PassageCitation(int Number, string ChunkId, string DocumentId, string Title,
    string SourceType, string? SourceUrl, int ChunkIndex, string? Section, int? PageNumber,
    string Quote, string RetrievalMethod = "hybrid");

public sealed record RetrievalDetails(string Query, string Scope, IReadOnlyList<string> DocumentIds,
    string? Section, int? PageNumber, int PassageCount, string? Notice = null)
{
    public string Method { get; init; } = "search";
    public IReadOnlyList<string> AttachedDocumentIds { get; init; } = [];
    public IReadOnlyList<DocumentVersion> DocumentVersions { get; init; } = [];
}

public sealed record DocumentVersion(string Id, DateTimeOffset UpdatedAt);

public sealed record ChatMessage(string Id, string Role, string Content, DateTimeOffset CreatedAt)
{
    public IReadOnlyList<PassageCitation> Citations { get; init; } = [];
    public RetrievalDetails? Retrieval { get; init; }
    public IReadOnlyList<string> FollowUpQuestions { get; init; } = [];
    /// <summary>The skill the user picked for this message, if any.</summary>
    public string? Skill { get; init; }
    public const string UserRole = "user";
    public const string AssistantRole = "assistant";
}

public static class DocumentStatus
{
    public const string Processing = "processing";
    public const string Ready = "ready";
    public const string Failed = "failed";
    public const string Deleted = "deleted";
}

public static class DocumentSource
{
    public const string Upload = "upload";
    public const string SharePoint = "sharepoint";
}

public sealed record UserDocument
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public required string FileName { get; init; }
    public required string SourceType { get; init; }
    public string? SourceUrl { get; init; }
    public long SizeBytes { get; init; }
    public string Status { get; init; } = DocumentStatus.Processing;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; init; }
    public int ChunkCount { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CleanupPending { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool AwaitingIngestion { get; init; }
}

public sealed record DocumentCleanupPage(IReadOnlyList<UserDocument> Documents, string? Continuation);

public sealed class CosmosOptions
{
    public string Endpoint { get; set; } = "";
    public string DatabaseName { get; set; } = "chatbot";
    public string ChatsContainer { get; set; } = "chats";
    public string DocumentsContainer { get; set; } = "documents";
}
