using Chatbot.Api.Services.Storage;

namespace Chatbot.Api.Services.Ingestion;

public sealed class DocumentCleanup(IDocumentRegistry registry, IDocumentIngestor ingestor, ILogger<DocumentCleanup> logger)
{
    public async Task<bool> TryCleanAsync(string userId, string documentId, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await registry.GetDeletedAsync(userId, documentId, cancellationToken);
            if (deleted is null) return false;
            await ingestor.DeleteAsync(userId, documentId, cancellationToken);
            // A writer that was active when cleanup began may have indexed after this delete.
            return !deleted.AwaitingIngestion && await registry.CompleteCleanupAsync(userId, documentId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Cleanup of deleted document {DocumentId} is pending and will be retried.", documentId);
            return false;
        }
    }
}

public sealed class DocumentCleanupWorker(IServiceScopeFactory scopes, ILogger<DocumentCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? continuation = null;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var registry = scope.ServiceProvider.GetRequiredService<IDocumentRegistry>();
                var cleanup = scope.ServiceProvider.GetRequiredService<DocumentCleanup>();
                var page = await registry.ReadCleanupPageAsync(continuation, stoppingToken);
                foreach (var document in page.Documents)
                {
                    await cleanup.TryCleanAsync(document.UserId, document.Id, stoppingToken);
                }
                continuation = page.Continuation;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Reading pending document cleanup failed; retrying on the next sweep.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
