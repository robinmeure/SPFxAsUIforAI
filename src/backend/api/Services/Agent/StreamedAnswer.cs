using System.Text;
using System.Text.Json;

namespace Chatbot.Api.Services.Agent;

internal sealed class StreamedAnswer
{
    private enum Stage { Start, Property, Name, Colon, Value, Answer, Done, Buffered }

    private readonly StringBuilder _json = new();
    private readonly StringBuilder _streamed = new();
    private Stage _stage;
    private int _nameIndex;
    private bool _escape;
    private int _unicodeDigits;
    private int _unicodeValue;
    private char? _highSurrogate;

    public bool HasContent => _json.Length > 0;

    public string Append(string fragment)
    {
        _json.Append(fragment);
        var text = new StringBuilder();
        foreach (var character in fragment)
        {
            switch (_stage)
            {
                case Stage.Start:
                    if (!IsWhitespace(character)) _stage = character == '{' ? Stage.Property : Stage.Buffered;
                    break;
                case Stage.Property:
                    if (!IsWhitespace(character)) _stage = character == '"' ? Stage.Name : Stage.Buffered;
                    break;
                case Stage.Name:
                    if (character == '"')
                        _stage = _nameIndex == "answer".Length ? Stage.Colon : Stage.Buffered;
                    else if (_nameIndex < "answer".Length && character == "answer"[_nameIndex])
                        _nameIndex++;
                    else
                        _stage = Stage.Buffered;
                    break;
                case Stage.Colon:
                    if (!IsWhitespace(character)) _stage = character == ':' ? Stage.Value : Stage.Buffered;
                    break;
                case Stage.Value:
                    if (!IsWhitespace(character)) _stage = character == '"' ? Stage.Answer : Stage.Buffered;
                    break;
                case Stage.Answer:
                    ReadAnswerCharacter(character, text);
                    break;
            }
        }

        var result = text.ToString();
        _streamed.Append(result);
        return result;
    }

    public (string Remaining, IReadOnlyList<string> Questions) Complete()
    {
        using var document = JsonDocument.Parse(_json.ToString());
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || root.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(new[] { "answer", "followUpQuestions", "offerFollowUps" }, StringComparer.Ordinal) is false
            || !root.TryGetProperty("answer", out var answerElement) || answerElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(answerElement.GetString())
            || !root.TryGetProperty("offerFollowUps", out var offer) || offer.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException("The response returned an invalid answer.");

        var answer = answerElement.GetString()!;
        var questions = SuggestedQuestions.Parse(root);
        if (_stage != Stage.Buffered && (_stage != Stage.Done || _streamed.ToString() != answer))
            throw new InvalidOperationException("The streamed answer did not match the completed response.");
        return (_stage == Stage.Buffered ? answer : "", offer.GetBoolean() ? questions : []);
    }

    private void ReadAnswerCharacter(char character, StringBuilder text)
    {
        if (_unicodeDigits > 0)
        {
            var value = character switch
            {
                >= '0' and <= '9' => character - '0',
                >= 'a' and <= 'f' => character - 'a' + 10,
                >= 'A' and <= 'F' => character - 'A' + 10,
                _ => throw new JsonException("Invalid Unicode escape in the streamed answer.")
            };
            _unicodeValue = (_unicodeValue << 4) + value;
            if (--_unicodeDigits == 0) AppendCharacter((char)_unicodeValue, text);
            return;
        }

        if (_escape)
        {
            _escape = false;
            if (character == 'u')
            {
                _unicodeDigits = 4;
                _unicodeValue = 0;
                return;
            }
            var decoded = character switch
            {
                '"' => '"', '\\' => '\\', '/' => '/',
                'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t',
                _ => throw new JsonException("Invalid escape in the streamed answer.")
            };
            AppendCharacter(decoded, text);
            return;
        }

        if (character == '\\') _escape = true;
        else if (character == '"')
        {
            if (_highSurrogate is not null) throw new JsonException("Incomplete Unicode character in the streamed answer.");
            _stage = Stage.Done;
        }
        else if (character < ' ') throw new JsonException("Unescaped control character in the streamed answer.");
        else AppendCharacter(character, text);
    }

    private void AppendCharacter(char character, StringBuilder text)
    {
        if (_highSurrogate is { } high)
        {
            if (!char.IsLowSurrogate(character)) throw new JsonException("Invalid Unicode character in the streamed answer.");
            text.Append(high).Append(character);
            _highSurrogate = null;
        }
        else if (char.IsHighSurrogate(character)) _highSurrogate = character;
        else if (char.IsLowSurrogate(character)) throw new JsonException("Invalid Unicode character in the streamed answer.");
        else text.Append(character);
    }

    private static bool IsWhitespace(char character) => character is ' ' or '\t' or '\r' or '\n';
}
