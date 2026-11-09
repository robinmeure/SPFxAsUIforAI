using Chatbot.Api.Models;
using Chatbot.Api.Services.Graph;
using Chatbot.Api.Services.Ingestion;
using Chatbot.Api.Services.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Chatbot.Api.Controllers;

/// <summary>
/// The user's own documents: uploaded from their device or selected in SharePoint/OneDrive. Files are queued for
/// extraction, chunking and embedding into the shared index, where they are only visible to their owner.
/// </summary>
[ApiController]
[Route("api/documents")]
public sealed class DocumentsController(
    IDocumentRegistry registry,
    DocumentCleanup cleanup,
    IngestionQueue queue,
    IGraphFiles graph,
    IOptions<IngestionOptions> options,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<DocumentsController> logger) : ControllerBase
{
    private static readonly TimeSpan ProcessingTimeout = TimeSpan.FromMinutes(15);

    [HttpGet]
    public async Task<IEnumerable<UserDocument>> List(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var documents = await registry.ListAsync(User.GetUserId(configuration), cancellationToken);
        // A restart drops queued jobs; report documents stuck in processing as failed so the user can add them again.
        return documents.Select(document => document.Status == DocumentStatus.Processing && now - document.UpdatedAt > ProcessingTimeout
            ? document with { Status = DocumentStatus.Failed, Error = "Processing was interrupted. Remove the document and add it again." }
            : document);
    }

    [HttpPost]
    [RequestSizeLimit(IngestionOptions.DefaultMaxFileSizeBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = IngestionOptions.DefaultMaxFileSizeBytes + 1024 * 1024)]
    public async Task<ActionResult<UserDocument>> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return Problem("Select a file to upload.", statusCode: StatusCodes.Status400BadRequest);
        }

        var validation = Validate(file.FileName, file.Length);
        if (validation is not null)
        {
            return validation;
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        return await EnqueueAsync(Path.GetFileName(file.FileName), DocumentSource.Upload, null, buffer.ToArray(), cancellationToken);
    }

    [HttpPost("sharepoint")]
    public async Task<ActionResult<UserDocument>> AddFromSharePoint(SharePointDocumentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var file = await graph.GetFileAsync(request.DriveId, request.ItemId, cancellationToken);
            var validation = Validate(file.Name, file.Size);
            if (validation is not null)
            {
                return validation;
            }

            var content = await graph.DownloadAsync(file.DriveId, file.ItemId, options.Value.MaxFileSizeBytes, cancellationToken);
            return await EnqueueAsync(file.Name, DocumentSource.SharePoint, file.WebUrl, content, cancellationToken);
        }
        catch (GraphException exception)
        {
            logger.LogWarning("Adding a SharePoint document failed with Graph status {Status}.", (int)exception.StatusCode);
            return Problem(exception.Message, statusCode: (int)exception.StatusCode is >= 400 and < 500
                ? (int)exception.StatusCode : StatusCodes.Status502BadGateway);
        }
    }

    [HttpDelete("{documentId:guid}")]
    public async Task<IActionResult> Delete(Guid documentId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId(configuration);
        var id = documentId.ToString();
        var deleted = await registry.MarkDeletedAsync(userId, id, cancellationToken);
        if (deleted is null)
        {
            return NotFound();
        }

        return await cleanup.TryCleanAsync(userId, id, cancellationToken)
            ? NoContent() : Accepted(new { detail = "The document is unavailable. Index cleanup is pending." });
    }

    private ActionResult? Validate(string fileName, long size)
    {
        if (!IngestionOptions.IsSupported(fileName))
        {
            return Problem($"Unsupported file type. Supported: {string.Join(", ", IngestionOptions.SupportedExtensions.Keys)}.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var maxBytes = options.Value.MaxFileSizeBytes;
        return size > maxBytes
            ? Problem($"The file is larger than {maxBytes / (1024 * 1024)} MB.", statusCode: StatusCodes.Status413PayloadTooLarge)
            : null;
    }

    private async Task<ActionResult<UserDocument>> EnqueueAsync(string fileName, string sourceType, string? sourceUrl, byte[] content,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var document = new UserDocument
        {
            Id = Guid.NewGuid().ToString(),
            UserId = User.GetUserId(configuration),
            FileName = fileName,
            SourceType = sourceType,
            SourceUrl = sourceUrl,
            SizeBytes = content.Length,
            Status = DocumentStatus.Processing,
            CreatedAt = now,
            UpdatedAt = now
        };
        await registry.CreateAsync(document, cancellationToken);
        if (!queue.TryEnqueue(new IngestionJob(document, content)))
        {
            await registry.MarkDeletedAsync(document.UserId, document.Id, cancellationToken);
            await registry.TryCompleteAsync(document with { Status = DocumentStatus.Failed }, cancellationToken);
            await cleanup.TryCleanAsync(document.UserId, document.Id, cancellationToken);
            return Problem("Too many documents are being processed. Try again in a few minutes.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Accepted(document);
    }
}
