using System.Text.Json.Serialization;
using Chatbot.Api.Services.Storage;

namespace Chatbot.Api.Services;

public sealed record ChatStreamEvent(
    [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonPropertyName("activity"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Activity = null,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null,
    [property: JsonPropertyName("chatId"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ChatId = null,
    [property: JsonPropertyName("citations"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<PassageCitation>? Citations = null,
    [property: JsonPropertyName("retrieval"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RetrievalDetails? Retrieval = null,
    [property: JsonPropertyName("followUpQuestions"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? FollowUpQuestions = null);

/// <summary>One user turn: the signed-in user, earlier messages of the chat (oldest first) and the new message.</summary>
public sealed record ChatTurn(string UserId, IReadOnlyList<ChatMessage> History, string Message)
{
    public IReadOnlyList<string> DocumentIds { get; init; } = [];
    /// <summary>A skill the user explicitly picked for this message; its instructions are applied directly.</summary>
    public string? Skill { get; init; }
}

public interface IChatService
{
    IAsyncEnumerable<ChatStreamEvent> StreamAsync(ChatTurn turn, CancellationToken cancellationToken);
}
