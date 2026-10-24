using System.Text.Json.Serialization;

namespace Chatbot.Api.Services;

public sealed record ChatStreamEvent(
    [property: JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null,
    [property: JsonPropertyName("activity"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Activity = null,
    [property: JsonPropertyName("error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Error = null);

public interface IChatService
{
    IAsyncEnumerable<ChatStreamEvent> StreamAsync(string message, CancellationToken cancellationToken);
}
