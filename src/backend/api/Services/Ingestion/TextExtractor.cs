using System.Text;
using Azure;
using Azure.AI.DocumentIntelligence;

namespace Chatbot.Api.Services.Ingestion;

public interface ITextExtractor
{
    /// <summary>Returns the document content as Markdown.</summary>
    Task<string> ExtractAsync(string fileName, byte[] content, CancellationToken cancellationToken);
}

/// <summary>Plain text and Markdown are decoded locally; other formats use the Document Intelligence layout model.</summary>
public sealed class DocumentIntelligenceTextExtractor(DocumentIntelligenceClient client) : ITextExtractor
{
    public async Task<string> ExtractAsync(string fileName, byte[] content, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is ".txt" or ".md")
        {
            return Encoding.UTF8.GetString(content).TrimStart('\uFEFF');
        }

        var operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed,
            new AnalyzeDocumentOptions("prebuilt-layout", BinaryData.FromBytes(content))
            {
                OutputContentFormat = DocumentContentFormat.Markdown
            }, cancellationToken);
        var result = operation.Value;
        var markdown = new StringBuilder(result.Content ?? "");
        // The .NET SDK requests UTF-16 spans, matching StringBuilder's offsets.
        foreach (var page in result.Pages.Where(page => page.Spans.Count > 0)
            .OrderByDescending(page => page.Spans.Min(span => span.Offset)))
        {
            markdown.Insert(page.Spans.Min(span => span.Offset), $"\n\n<!-- SourcePage: {page.PageNumber} -->\n\n");
        }
        return markdown.ToString();
    }
}
