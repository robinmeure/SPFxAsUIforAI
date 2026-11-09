using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Chatbot.Api.Services.Graph;
using Chatbot.Api.Services.Ingestion;
using Chatbot.Api.Services.Storage;

namespace Chatbot.Api.Services.Agent;

public sealed class ListMyDocumentsTool(IDocumentRegistry registry) : IAgentTool
{
    public string Name => "list_my_documents";

    public string Description => "Lists documents attached to this conversation, with their documentId and status.";

    public BinaryData Parameters { get; } = BinaryData.FromString("""
        { "type": "object", "properties": {}, "required": [], "additionalProperties": false }
        """);

    public string DescribeActivity(JsonElement arguments) => "Checking your documents";

    public async Task<string> InvokeAsync(JsonElement arguments, AgentContext context, CancellationToken cancellationToken)
    {
        var documents = await registry.ListAsync(context.UserId, cancellationToken);
        return ToolJson.Serialize(new
        {
            documents = documents.Where(document => context.DocumentIds.Contains(document.Id)).Select(document => new
            {
                documentId = document.Id,
                fileName = document.FileName,
                status = document.Status,
                source = document.SourceType,
                addedAt = document.CreatedAt
            })
        });
    }
}

public sealed class LoadSkillTool(SkillCatalog catalog) : IAgentTool
{
    public string Name => "load_skill";

    public string Description => "Loads the full instructions of a skill listed under 'Available skills'. Call this before following a skill.";

    public BinaryData Parameters { get; } = BinaryData.FromString("""
        {
          "type": "object",
          "properties": { "name": { "type": "string", "description": "The skill name." } },
          "required": ["name"],
          "additionalProperties": false
        }
        """);

    public string DescribeActivity(JsonElement arguments) => $"Using skill: {ToolJson.GetString(arguments, "name")}";

    public Task<string> InvokeAsync(JsonElement arguments, AgentContext context, CancellationToken cancellationToken)
    {
        var skill = catalog.Find(ToolJson.RequireString(arguments, "name")) ?? throw new ToolException("Unknown skill.");
        return Task.FromResult(ToolJson.Serialize(new
        {
            name = skill.Name,
            instructions = skill.Instructions,
            resources = skill.ResourcePaths.Keys.Order()
        }));
    }
}

public sealed class ReadSkillResourceTool(SkillCatalog catalog) : IAgentTool
{
    public string Name => "read_skill_resource";

    public string Description => "Reads a reference or asset file that belongs to a loaded skill, by the relative path listed in its resources.";

    public BinaryData Parameters { get; } = BinaryData.FromString("""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "The skill name." },
            "path": { "type": "string", "description": "Relative resource path, e.g. references/guide.md." }
          },
          "required": ["name", "path"],
          "additionalProperties": false
        }
        """);

    public string DescribeActivity(JsonElement arguments) => "Reading skill resources";

    public Task<string> InvokeAsync(JsonElement arguments, AgentContext context, CancellationToken cancellationToken)
    {
        var skill = catalog.Find(ToolJson.RequireString(arguments, "name")) ?? throw new ToolException("Unknown skill.");
        var content = catalog.ReadResource(skill, ToolJson.RequireString(arguments, "path"))
            ?? throw new ToolException("Unknown resource. Use a path from the skill's resources list.");
        return Task.FromResult(content);
    }
}

public sealed partial class CreateDocumentTool(IGraphFiles graph) : IAgentTool
{
    public string Name => "create_document";

    public string Description =>
        "Creates a Word (docx), PDF (pdf) or Excel (xlsx) document and saves it to the user's SharePoint/OneDrive. " +
        "For docx and pdf provide 'markdown' (headings, paragraphs, lists, pipe tables, **bold**). " +
        "For xlsx provide 'sheets'. Returns the file name and link.";

    public BinaryData Parameters { get; } = BinaryData.FromString("""
        {
          "type": "object",
          "properties": {
            "format": { "type": "string", "enum": ["docx", "pdf", "xlsx"] },
            "fileName": { "type": "string", "description": "File name without path; the extension is added if missing." },
            "title": { "type": "string", "description": "Document title (docx/pdf)." },
            "markdown": { "type": ["string", "null"], "description": "Body content for docx/pdf; null for xlsx." },
            "sheets": {
              "type": ["array", "null"],
              "description": "Worksheets for xlsx; null for docx/pdf.",
              "items": {
                "type": "object",
                "properties": {
                  "name": { "type": "string" },
                  "columns": { "type": "array", "items": { "type": "string" } },
                  "rows": { "type": "array", "items": { "type": "array", "items": { "type": "string" } } }
                },
                "required": ["name", "columns", "rows"],
                "additionalProperties": false
              }
            }
          },
          "required": ["format", "fileName", "title", "markdown", "sheets"],
          "additionalProperties": false
        }
        """);

    public string DescribeActivity(JsonElement arguments) => $"Creating {ToolJson.GetString(arguments, "format")?.ToUpperInvariant()} document";

    public async Task<string> InvokeAsync(JsonElement arguments, AgentContext context, CancellationToken cancellationToken)
    {
        var format = ToolJson.RequireString(arguments, "format").ToLowerInvariant();
        var title = ToolJson.GetString(arguments, "title") ?? "";
        var fileName = SanitizeFileName(ToolJson.RequireString(arguments, "fileName"), format);

        DriveFile file = format switch
        {
            "docx" => await graph.UploadAsync(fileName,
                OfficeDocumentBuilder.CreateDocx(title, ToolJson.RequireString(arguments, "markdown")),
                OfficeDocumentBuilder.DocxContentType, cancellationToken),
            "xlsx" => await graph.UploadAsync(fileName, OfficeDocumentBuilder.CreateXlsx(ReadSheets(arguments)),
                OfficeDocumentBuilder.XlsxContentType, cancellationToken),
            "pdf" => await CreatePdfAsync(fileName, title, ToolJson.RequireString(arguments, "markdown"), cancellationToken),
            _ => throw new ToolException("Unsupported format. Use docx, pdf or xlsx.")
        };

        context.Files.Add(new GeneratedFile(file.Name, file.WebUrl));
        return ToolJson.Serialize(new { saved = true, name = file.Name, url = file.WebUrl });
    }

    /// <summary>Graph converts Office files to PDF: upload a temporary DOCX, download it as PDF, then remove the DOCX.</summary>
    private async Task<DriveFile> CreatePdfAsync(string fileName, string title, string markdown, CancellationToken cancellationToken)
    {
        var docx = OfficeDocumentBuilder.CreateDocx(title, markdown);
        var temporary = await graph.UploadAsync($"tmp-{Path.GetFileNameWithoutExtension(fileName)}-{Guid.NewGuid():N}.docx",
            docx, OfficeDocumentBuilder.DocxContentType, cancellationToken);
        try
        {
            var pdf = await graph.ConvertToPdfAsync(temporary.DriveId, temporary.ItemId, cancellationToken);
            return await graph.UploadAsync(fileName, pdf, OfficeDocumentBuilder.PdfContentType, cancellationToken);
        }
        finally
        {
            await graph.DeleteAsync(temporary.DriveId, temporary.ItemId, CancellationToken.None);
        }
    }

    private static List<SheetData> ReadSheets(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("sheets", out var sheets) || sheets.ValueKind != JsonValueKind.Array || sheets.GetArrayLength() == 0)
        {
            throw new ToolException("'sheets' is required for xlsx.");
        }

        return sheets.EnumerateArray().Select(sheet => new SheetData(
            sheet.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
            sheet.TryGetProperty("columns", out var columns) ? columns.EnumerateArray().Select(c => c.ToString()).ToList() : [],
            sheet.TryGetProperty("rows", out var rows)
                ? rows.EnumerateArray().Select(row => (IReadOnlyList<string?>)row.EnumerateArray().Select(cell => (string?)cell.ToString()).ToList()).ToList()
                : [])).ToList();
    }

    internal static string SanitizeFileName(string fileName, string format)
    {
        var name = InvalidFileNameRegex().Replace(fileName, "").Trim(' ', '.');
        name = OfficeExtensionRegex().Replace(name, "").Trim(' ', '.');
        if (name.Length == 0)
        {
            name = "document";
        }

        var extension = "." + format;
        name = name.Length > 120 - extension.Length ? name[..(120 - extension.Length)].TrimEnd(' ', '.') : name;
        return name + extension;
    }

    [GeneratedRegex(@"\.(docx|pdf|xlsx)$", RegexOptions.IgnoreCase)]
    private static partial Regex OfficeExtensionRegex();

    [GeneratedRegex(@"[""*:<>?/\\|#%~\x00-\x1F]")]
    private static partial Regex InvalidFileNameRegex();
}
