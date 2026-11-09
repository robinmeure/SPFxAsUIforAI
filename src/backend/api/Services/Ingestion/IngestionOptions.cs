namespace Chatbot.Api.Services.Ingestion;

public sealed class IngestionOptions
{
    public const long DefaultMaxFileSizeBytes = 10 * 1024 * 1024;

    public long MaxFileSizeBytes { get; set; } = DefaultMaxFileSizeBytes;
    public int ChunkSizeCharacters { get; set; } = 2000;
    public int ChunkOverlapCharacters { get; set; } = 300;

    public static readonly IReadOnlyDictionary<string, string> SupportedExtensions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".txt"] = "text/plain",
        [".md"] = "text/markdown",
    };

    public static bool IsSupported(string fileName) => SupportedExtensions.ContainsKey(Path.GetExtension(fileName));
}
