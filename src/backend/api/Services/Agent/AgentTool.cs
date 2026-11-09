using System.Text.Json;
using Chatbot.Api.Services.Storage;

namespace Chatbot.Api.Services.Agent;

public sealed record GeneratedFile(string Name, string Url);

/// <summary>Per-turn state shared by the tools: who is asking, which sources were shown to the model, which files were made.</summary>
public sealed class AgentContext(string userId, IReadOnlyList<string>? documentIds = null)
{
    public string UserId { get; } = userId;
    public IReadOnlyList<string> DocumentIds { get; } = documentIds ?? [];
    public List<PassageCitation> Sources { get; } = [];
    public List<GeneratedFile> Files { get; } = [];
}

public interface IAgentTool
{
    string Name { get; }
    string Description { get; }

    /// <summary>JSON schema in OpenAI strict mode: every property required, nullable via <c>["type","null"]</c>.</summary>
    BinaryData Parameters { get; }

    /// <summary>Short progress label shown to the user while the tool runs.</summary>
    string DescribeActivity(JsonElement arguments);

    Task<string> InvokeAsync(JsonElement arguments, AgentContext context, CancellationToken cancellationToken);
}

/// <summary>A failure the model may see and react to (for example, "file not found").</summary>
public sealed class ToolException(string message) : Exception(message);

internal static class ToolJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static string? GetString(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string RequireString(JsonElement arguments, string name) =>
        GetString(arguments, name) is { Length: > 0 } value ? value : throw new ToolException($"'{name}' is required.");

    public static string Truncate(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength];
}
