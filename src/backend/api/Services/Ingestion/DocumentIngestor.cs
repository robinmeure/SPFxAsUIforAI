using System.Threading.Channels;
using Chatbot.Api.Services.Storage;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

namespace Chatbot.Api.Services.Ingestion;

public interface IEmbeddingGenerator
{
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken);
}

public sealed class OpenAIEmbeddingGenerator(EmbeddingClient client, IOptions<RagOptions> options) : IEmbeddingGenerator
{
    private const int BatchSize = 16;

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken)
    {
        var vectors = new List<ReadOnlyMemory<float>>(inputs.Count);
        var generationOptions = new EmbeddingGenerationOptions { Dimensions = options.Value.EmbeddingDimensions };
        foreach (var batch in inputs.Chunk(BatchSize))
        {
            var result = await client.GenerateEmbeddingsAsync(batch, generationOptions, cancellationToken);
            vectors.AddRange(result.Value.OrderBy(embedding => embedding.Index).Select(embedding => embedding.ToFloats()));
        }

        return vectors;
    }
}

public interface IDocumentIngestor
{
    /// <summary>Extracts, chunks, embeds and indexes the document; returns the number of chunks.</summary>
    Task<int> IngestAsync(UserDocument document, byte[] content, CancellationToken cancellationToken);
    Task DeleteAsync(string userId, string documentId, CancellationToken cancellationToken);
}

public sealed class DocumentIngestor(
    ITextExtractor extractor,
    IEmbeddingGenerator embeddings,
    KnowledgeIndex index,
    IOptions<IngestionOptions> options) : IDocumentIngestor
{
    public async Task<int> IngestAsync(UserDocument document, byte[] content, CancellationToken cancellationToken)
    {
        var markdown = await extractor.ExtractAsync(document.FileName, content, cancellationToken);
        var chunks = MarkdownChunker.ChunkWithMetadata(markdown, options.Value.ChunkSizeCharacters, options.Value.ChunkOverlapCharacters);
        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("The document contains no extractable text.");
        }

        var vectors = await embeddings.EmbedAsync(chunks.Select(chunk => chunk.Content).ToArray(), cancellationToken);
        await index.DeleteDocumentChunksAsync(document.UserId, document.Id, cancellationToken);
        await index.IndexChunksAsync(document, chunks, vectors, cancellationToken);
        return chunks.Count;
    }

    public Task DeleteAsync(string userId, string documentId, CancellationToken cancellationToken) =>
        index.DeleteDocumentChunksAsync(userId, documentId, cancellationToken);
}

/// <summary>Used with mock responses: registers documents without calling Azure.</summary>
public sealed class MockDocumentIngestor : IDocumentIngestor
{
    public Task<int> IngestAsync(UserDocument document, byte[] content, CancellationToken cancellationToken) => Task.FromResult(1);

    public Task DeleteAsync(string userId, string documentId, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed record IngestionJob(UserDocument Document, byte[] Content);

public sealed class IngestionQueue
{
    private readonly Channel<IngestionJob> _channel = Channel.CreateBounded<IngestionJob>(
        new BoundedChannelOptions(50) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false });

    public bool TryEnqueue(IngestionJob job) => _channel.Writer.TryWrite(job);

    public IAsyncEnumerable<IngestionJob> ReadAllAsync(CancellationToken cancellationToken) => _channel.Reader.ReadAllAsync(cancellationToken);
}

/// <summary>
/// Processes queued documents after the upload request has returned. Downloads from SharePoint happen in the request
/// (they need the user's token); extraction and indexing here use the API's managed identity.
/// </summary>
public sealed class IngestionWorker(
    IngestionQueue queue,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    private const int Concurrency = 2;

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(Enumerable.Range(0, Concurrency).Select(_ => ProcessAsync(stoppingToken)));

    private async Task ProcessAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in queue.ReadAllAsync(stoppingToken))
        {
            using var scope = scopes.CreateScope();
            var registry = scope.ServiceProvider.GetRequiredService<IDocumentRegistry>();
            var ingestor = scope.ServiceProvider.GetRequiredService<IDocumentIngestor>();
            var cleanup = scope.ServiceProvider.GetRequiredService<DocumentCleanup>();
            UserDocument result;
            try
            {
                if (await registry.GetAsync(job.Document.UserId, job.Document.Id, stoppingToken) is null)
                {
                    await registry.TryCompleteAsync(job.Document with { Status = DocumentStatus.Failed }, stoppingToken);
                    await cleanup.TryCleanAsync(job.Document.UserId, job.Document.Id, stoppingToken);
                    continue;
                }
                var chunkCount = await ingestor.IngestAsync(job.Document, job.Content, stoppingToken);
                result = job.Document with { Status = DocumentStatus.Ready, ChunkCount = chunkCount, UpdatedAt = time.GetUtcNow() };
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Ingestion of document {DocumentId} failed.", job.Document.Id);
                result = job.Document with
                {
                    Status = DocumentStatus.Failed,
                    Error = "The document could not be processed.",
                    UpdatedAt = time.GetUtcNow()
                };
            }

            try
            {
                if (!await registry.TryCompleteAsync(result, stoppingToken))
                {
                    await cleanup.TryCleanAsync(job.Document.UserId, job.Document.Id, stoppingToken);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Updating the status of document {DocumentId} failed.", job.Document.Id);
            }
        }
    }
}
