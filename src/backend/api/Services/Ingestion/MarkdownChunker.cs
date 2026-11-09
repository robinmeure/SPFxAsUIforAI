using System.Text;
using System.Text.RegularExpressions;

namespace Chatbot.Api.Services.Ingestion;

public sealed record DocumentChunk(string Content, string? Section, IReadOnlyList<string> SectionPaths, int? PageNumber);

/// <summary>
/// Splits Markdown into chunks of roughly <c>maxChars</c> characters on paragraph boundaries.
/// Chunks that don't start with a heading are prefixed with the current heading path so each chunk keeps its context.
/// </summary>
public static partial class MarkdownChunker
{
    public static IReadOnlyList<string> Chunk(string markdown, int maxChars = 2000, int overlapChars = 300)
        => ChunkWithMetadata(markdown, maxChars, overlapChars).Select(chunk => chunk.Content).ToList();

    public static IReadOnlyList<DocumentChunk> ChunkWithMetadata(string markdown, int maxChars = 2000, int overlapChars = 300)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 200);
        overlapChars = Math.Clamp(overlapChars, 0, maxChars / 2);

        var chunks = new List<DocumentChunk>();
        var headings = new string?[6];
        var current = new StringBuilder();
        var currentContext = "";
        var previousTail = "";
        int? pageNumber = null;

        void Flush()
        {
            var body = current.ToString().Trim();
            if (body.Length == 0)
            {
                return;
            }

            var prefix = currentContext.Length > 0 && !body.StartsWith('#') ? $"{currentContext}\n\n" : "";
            var overlap = previousTail.Length > 0 ? $"…{previousTail.Replace("\n\n", "\n")}\n\n" : "";
            var path = headings.Where(h => !string.IsNullOrEmpty(h)).Select(h => h!).ToArray();
            var paths = Enumerable.Range(1, path.Length).Select(length => string.Join(" > ", path.Take(length))).ToArray();
            chunks.Add(new DocumentChunk(prefix + overlap + body, paths.LastOrDefault(), paths, pageNumber));
            previousTail = Tail(body, overlapChars);
            current.Clear();
        }

        foreach (var block in SplitBlocks(markdown))
        {
            var page = PageRegex().Match(block);
            if (page.Success)
            {
                Flush();
                previousTail = "";
                pageNumber = int.Parse(page.Groups[1].Value);
                continue;
            }

            var heading = HeadingRegex().Match(block);
            if (heading.Success)
            {
                var level = heading.Groups[1].Value.Length;
                if (current.Length > 0)
                {
                    Flush();
                    previousTail = "";
                }

                headings[level - 1] = heading.Groups[2].Value.Trim();
                Array.Clear(headings, level, headings.Length - level);
            }

            foreach (var piece in SplitOversized(block, maxChars))
            {
                if (current.Length > 0 && current.Length + piece.Length + 2 > maxChars)
                {
                    Flush();
                }

                if (current.Length == 0)
                {
                    currentContext = heading.Success ? "" : string.Join(" > ", headings.Where(h => !string.IsNullOrEmpty(h)));
                }

                current.Append(piece).Append("\n\n");
            }
        }

        Flush();
        return chunks;
    }

    private static IEnumerable<string> SplitBlocks(string markdown)
    {
        var cleaned = HtmlCommentRegex().Replace(markdown.Replace("\r\n", "\n"), match =>
            PageRegex().IsMatch(match.Value) ? $"\n\n{match.Value}\n\n" : "");
        return BlankLineRegex().Split(cleaned).Select(block => block.Trim()).Where(block => block.Length > 0);
    }

    private static IEnumerable<string> SplitOversized(string block, int maxChars)
    {
        var remaining = block;
        while (remaining.Length > maxChars)
        {
            var cut = remaining.LastIndexOfAny(['\n', '.', '!', '?'], maxChars - 1, maxChars / 2);
            if (cut < 0)
            {
                cut = remaining.LastIndexOf(' ', maxChars - 1, maxChars / 2);
            }

            cut = cut < 0 ? maxChars : cut + 1;
            yield return remaining[..cut].Trim();
            remaining = remaining[cut..].TrimStart();
        }

        if (remaining.Length > 0)
        {
            yield return remaining;
        }
    }

    private static string Tail(string text, int length)
    {
        if (length == 0 || text.Length <= length)
        {
            return length == 0 ? "" : text;
        }

        var tail = text[^length..];
        var space = tail.IndexOf(' ');
        return space >= 0 && space < tail.Length - 1 ? tail[(space + 1)..] : tail;
    }

    [GeneratedRegex(@"^(#{1,6})[ \t]+([^\n]+)")]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^<!-- SourcePage: (\d+) -->$")]
    private static partial Regex PageRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentRegex();

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex BlankLineRegex();
}
