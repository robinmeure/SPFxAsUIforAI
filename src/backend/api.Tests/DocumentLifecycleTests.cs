using System.Net;
using Chatbot.Api.Services.Ingestion;
using Chatbot.Api.Services.Storage;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Chatbot.Api.Tests;

public sealed class DocumentLifecycleTests
{
    private static UserDocument Document() => new()
    {
        Id = Guid.NewGuid().ToString(), UserId = "owner", FileName = "notes.txt", SourceType = DocumentSource.Upload
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeletingWhileIngestingCannotPublishSuccessOrFailure(bool fails)
    {
        var registry = new InMemoryDocumentRegistry();
        var document = Document();
        await registry.CreateAsync(document, default);
        var ingestor = new ControlledIngestor { Fails = fails };
        var queue = new IngestionQueue();
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IDocumentRegistry>(registry).AddSingleton<IDocumentIngestor>(ingestor)
            .AddScoped<DocumentCleanup>().BuildServiceProvider();
        using var worker = new IngestionWorker(queue, services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, NullLogger<IngestionWorker>.Instance);
        queue.TryEnqueue(new(document, []));
        await worker.StartAsync(default);
        try
        {
            await ingestor.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await registry.MarkDeletedAsync(document.UserId, document.Id, default);
            Assert.Null(await registry.GetAsync(document.UserId, document.Id, default));
            ingestor.Release.SetResult();
            await ingestor.Deleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            ingestor.Release.TrySetResult();
            await worker.StopAsync(default);
        }
        Assert.Empty(await registry.ListAsync(document.UserId, default));
        Assert.False(await registry.TryCompleteAsync(document with { Status = DocumentStatus.Ready }, default));
        Assert.False(ingestor.Indexed);
    }

    [Fact]
    public async Task DeletingBeforeTheJobStartsSkipsExtractionAndAcknowledgesCleanup()
    {
        var registry = new InMemoryDocumentRegistry();
        var document = Document();
        await registry.CreateAsync(document, default);
        await registry.MarkDeletedAsync(document.UserId, document.Id, default);
        var ingestor = new ControlledIngestor();
        var queue = new IngestionQueue();
        using var services = new ServiceCollection().AddLogging()
            .AddSingleton<IDocumentRegistry>(registry).AddSingleton<IDocumentIngestor>(ingestor)
            .AddScoped<DocumentCleanup>().BuildServiceProvider();
        using var worker = new IngestionWorker(queue, services.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, NullLogger<IngestionWorker>.Instance);
        queue.TryEnqueue(new(document, []));
        await worker.StartAsync(default);
        try { await ingestor.Deleted.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { await worker.StopAsync(default); }
        Assert.False(ingestor.Started.Task.IsCompleted);
        Assert.Null(await registry.GetAsync(document.UserId, document.Id, default));
    }

    [Fact]
    public async Task InFlightCleanupCannotClearPendingStateForALateWriter()
    {
        var registry = new InMemoryDocumentRegistry();
        var document = Document();
        await registry.CreateAsync(document, default);
        await registry.MarkDeletedAsync(document.UserId, document.Id, default);
        var deleteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishDelete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ingestor = new Mock<IDocumentIngestor>();
        ingestor.Setup(x => x.DeleteAsync(document.UserId, document.Id, It.IsAny<CancellationToken>()))
            .Returns(async () => { deleteStarted.SetResult(); await finishDelete.Task; });
        var cleanup = new DocumentCleanup(registry, ingestor.Object, NullLogger<DocumentCleanup>.Instance);
        var cleaning = cleanup.TryCleanAsync(document.UserId, document.Id, default);
        await deleteStarted.Task;
        Assert.False(await registry.TryCompleteAsync(document with { Status = DocumentStatus.Ready }, default));
        finishDelete.SetResult();
        Assert.False(await cleaning);
        Assert.Single((await registry.ReadCleanupPageAsync(null, default)).Documents);

        ingestor.Setup(x => x.DeleteAsync(document.UserId, document.Id, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Temporary failure"));
        Assert.False(await cleanup.TryCleanAsync(document.UserId, document.Id, default));
        var restartedCleanup = new DocumentCleanup(registry, new MockDocumentIngestor(), NullLogger<DocumentCleanup>.Instance);
        Assert.True(await restartedCleanup.TryCleanAsync(document.UserId, document.Id, default));
        Assert.Empty((await registry.ReadCleanupPageAsync(null, default)).Documents);
        Assert.NotNull(await registry.MarkDeletedAsync(document.UserId, document.Id, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.CreateAsync(document, default));
    }

    [Fact]
    public async Task CompletingBeforeDeletionAndWrongOwnerAreHandled()
    {
        var registry = new InMemoryDocumentRegistry();
        var document = Document();
        await registry.CreateAsync(document, default);
        Assert.True(await registry.TryCompleteAsync(document with { Status = DocumentStatus.Ready, ChunkCount = 3 }, default));
        Assert.Null(await registry.MarkDeletedAsync("other-owner", document.Id, default));
        var deleted = await registry.MarkDeletedAsync(document.UserId, document.Id, default);
        Assert.False(deleted!.AwaitingIngestion);
        Assert.True(await registry.CompleteCleanupAsync(document.UserId, document.Id, default));
        Assert.False(await registry.TryCompleteAsync(Document() with { Status = DocumentStatus.Ready }, default));
    }

    [Fact]
    public async Task CosmosCompletionRetriesEtagConflictAndDoesNotResurrectDeletion()
    {
        var document = Document();
        var deleted = document with { Status = DocumentStatus.Deleted, CleanupPending = true, AwaitingIngestion = true };
        var container = new Mock<Container>();
        container.SetupSequence(c => c.ReadItemAsync<UserDocument>(document.Id, new PartitionKey(document.UserId),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(document, "before-delete"))
            .ReturnsAsync(Response(deleted, "after-delete"));
        var replacements = new List<(UserDocument Document, string Etag)>();
        container.Setup(c => c.ReplaceItemAsync(It.IsAny<UserDocument>(), document.Id, new PartitionKey(document.UserId),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .Returns<UserDocument, string, PartitionKey?, ItemRequestOptions, CancellationToken>((value, _, _, options, _) =>
            {
                replacements.Add((value, options.IfMatchEtag));
                if (options.IfMatchEtag == "before-delete")
                    throw new CosmosException("Concurrent delete", HttpStatusCode.PreconditionFailed, 0, "", 0);
                return Task.FromResult(Response(value, "acknowledged"));
            });
        var client = new Mock<CosmosClient>();
        client.Setup(c => c.GetContainer("chatbot", "documents")).Returns(container.Object);
        var registry = new CosmosDocumentRegistry(client.Object, Options.Create(new CosmosOptions()));
        Assert.False(await registry.TryCompleteAsync(document with { Status = DocumentStatus.Ready }, default));
        Assert.Equal(2, replacements.Count);
        Assert.Equal("after-delete", replacements[1].Etag);
        Assert.Equal(DocumentStatus.Deleted, replacements[1].Document.Status);
        Assert.False(replacements[1].Document.AwaitingIngestion);
        Assert.True(replacements[1].Document.CleanupPending);
        container.Verify(c => c.UpsertItemAsync(It.IsAny<UserDocument>(), It.IsAny<PartitionKey?>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ItemResponse<UserDocument> Response(UserDocument document, string etag)
    {
        var response = new Mock<ItemResponse<UserDocument>>();
        response.SetupGet(r => r.Resource).Returns(document);
        response.SetupGet(r => r.ETag).Returns(etag);
        return response.Object;
    }

    private sealed class ControlledIngestor : IDocumentIngestor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Deleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Fails { get; init; }
        public bool Indexed { get; private set; }

        public async Task<int> IngestAsync(UserDocument document, byte[] content, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Release.Task.WaitAsync(cancellationToken);
            Indexed = true;
            if (Fails) throw new InvalidOperationException("Extraction failed");
            return 1;
        }

        public Task DeleteAsync(string userId, string documentId, CancellationToken cancellationToken)
        {
            Indexed = false;
            Deleted.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
