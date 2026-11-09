using System.ComponentModel.DataAnnotations;
using Chatbot.Api.Services.Storage;

namespace Chatbot.Api.Models;

public sealed record ChatRequest(
    [Required, StringLength(8000, MinimumLength = 1)] string Message,
    [RegularExpression("^[0-9a-fA-F-]{36}$")] string? ChatId = null,
    [MaxLength(ChatDocumentSets.MaxDocuments)] IReadOnlyList<string>? DocumentIds = null,
    [StringLength(64), RegularExpression("^[a-z0-9]+(-[a-z0-9]+)*$")] string? Skill = null);

public sealed record ChatDocumentsRequest([Required, MaxLength(ChatDocumentSets.MaxDocuments)] IReadOnlyList<string> DocumentIds);

public sealed record ChatDocumentsResponse(IReadOnlyList<string> DocumentIds, ChatMessageDto? Event);

public sealed record SharePointDocumentRequest(
    [Required, StringLength(256, MinimumLength = 1)] string DriveId,
    [Required, StringLength(256, MinimumLength = 1)] string ItemId);

public sealed record ChatDetails(string Id, string Title, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<ChatMessageDto> Messages, IReadOnlyList<string> DocumentIds, IReadOnlyList<UserDocument> Documents);

public sealed record ChatMessageDto(string Id, string Role, string Content, DateTimeOffset CreatedAt,
    IReadOnlyList<PassageCitation> Citations, RetrievalDetails? Retrieval, string? Skill = null)
{
    public IReadOnlyList<string> FollowUpQuestions { get; init; } = [];
}
