using System.Text.Json;

namespace Chatbot.Api.Services.Agent;

internal static class SuggestedQuestions
{
    public static readonly BinaryData ResponseSchema = BinaryData.FromString("""
        {
          "type": "object",
          "properties": {
            "answer": { "type": "string" },
            "followUpQuestions": {
              "type": "array", "minItems": 1, "maxItems": 3,
              "items": { "type": "string", "minLength": 1, "maxLength": 240 }
            },
            "offerFollowUps": { "type": "boolean" }
          },
          "required": ["answer", "followUpQuestions", "offerFollowUps"],
          "additionalProperties": false
        }
        """);

    internal static IReadOnlyList<string> Parse(JsonElement response)
    {
        if (response.ValueKind != JsonValueKind.Object
            || !response.TryGetProperty("followUpQuestions", out var questions) || questions.ValueKind != JsonValueKind.Array
            || questions.GetArrayLength() is < 1 or > 3
            || questions.EnumerateArray().Any(question => question.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(question.GetString()) || question.GetString()!.Length > 240))
            throw new InvalidOperationException("The response returned invalid follow-up questions.");

        return questions.EnumerateArray().Select(question => question.GetString()!.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
