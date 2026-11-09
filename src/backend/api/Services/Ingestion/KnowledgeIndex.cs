using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using Chatbot.Api.Services.Storage;
using Microsoft.Extensions.Options;

namespace Chatbot.Api.Services.Ingestion;

public sealed record SearchHit(string Id, string DocumentId, string Title, string? SourceUrl, string Content,
    string SourceType, int ChunkIndex, string? Section, int? PageNumber);

/// <summary>
/// The index of the users' own documents. Every chunk carries the owner's object ID and every query is trimmed to
/// <c>ownerId eq '&lt;user&gt;'</c>, so users only ever retrieve their own documents.
/// </summary>
public sealed class KnowledgeIndex(SearchClient searchClient, IEmbeddingGenerator embeddings)
{
    public const string SemanticConfigurationName = "semantic";
    private const string VectorProfileName = "default";
    private const string VectorAlgorithmName = "hnsw";
    private const int UploadBatchSize = 100;
    private static readonly string[] SelectFields =
        ["id", "documentId", "fileName", "sourceUrl", "content", "sourceType", "chunkIndex", "sectionPath", "pageNumber"];

    /// <summary>The index definition the API creates or updates at startup.</summary>
    public static SearchIndex CreateDefinition(string name, int embeddingDimensions) => new(name)
    {
        Fields =
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true, IsFilterable = true },
            new SimpleField("documentId", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("ownerId", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("chunkIndex", SearchFieldDataType.Int32) { IsFilterable = true, IsSortable = true },
            new SearchableField("fileName") { IsFilterable = true },
            new SimpleField("sourceType", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("sourceUrl", SearchFieldDataType.String),
            new SimpleField("sectionPath", SearchFieldDataType.String),
            new SimpleField("sectionPaths", SearchFieldDataType.Collection(SearchFieldDataType.String))
                { IsFilterable = true, IsFacetable = true },
            new SimpleField("pageNumber", SearchFieldDataType.Int32) { IsFilterable = true },
            new SearchableField("content"),
            new VectorSearchField("contentVector", embeddingDimensions, VectorProfileName) { IsHidden = true }
        },
        VectorSearch = new VectorSearch
        {
            Profiles = { new VectorSearchProfile(VectorProfileName, VectorAlgorithmName) },
            Algorithms = { new HnswAlgorithmConfiguration(VectorAlgorithmName) }
        },
        SemanticSearch = new SemanticSearch
        {
            Configurations =
            {
                new SemanticConfiguration(SemanticConfigurationName, new SemanticPrioritizedFields
                {
                    TitleField = new SemanticField("fileName"),
                    ContentFields = { new SemanticField("content") }
                })
            }
        }
    };

    public async Task<IReadOnlyList<string>> GetSectionsAsync(string userId, IReadOnlyList<string> documentIds,
        CancellationToken cancellationToken)
    {
        var searchOptions = new SearchOptions
        {
            Size = 0,
            Filter = ScopeFilter(userId, documentIds),
            Facets = { "sectionPaths,count:1000" }
        };

        var results = await searchClient.SearchAsync<SearchDocument>("*", searchOptions, cancellationToken);
        
        return results.Value.Facets is { } facets && facets.TryGetValue("sectionPaths", out var sections)
            ? sections.Select(section => section.Value?.ToString()).OfType<string>().ToArray() : [];
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string userId, string query, IReadOnlyList<string> documentIds, int size,
        CancellationToken cancellationToken, string? section = null, int? pageNumber = null)
    {
        var vector = (await embeddings.EmbedAsync([query], cancellationToken))[0];
        var options = new SearchOptions
        {
            Size = size,
            Filter = ScopeFilter(userId, documentIds, section, pageNumber),
            QueryType = SearchQueryType.Semantic,
            SemanticSearch = new SemanticSearchOptions { SemanticConfigurationName = SemanticConfigurationName },
            VectorSearch = new VectorSearchOptions
            {
                FilterMode = VectorFilterMode.PreFilter,
                Queries = { new VectorizedQuery(vector) { Fields = { "contentVector" }, KNearestNeighborsCount = 50 } }
            }
        };
        foreach (var field in SelectFields)
        {
            options.Select.Add(field);
        }

        var results = await searchClient.SearchAsync<SearchDocument>(query, options, cancellationToken);
        var hits = new List<SearchHit>();
        await foreach (var result in results.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            var hit = ToHit(result.Document);
            if (!string.IsNullOrWhiteSpace(hit.Content))
            {
                hits.Add(hit);
            }
        }

        return hits;
    }

    /// <summary>Returns the chunks of one of the user's documents in their original order.</summary>
    public async Task<IReadOnlyList<SearchHit>> GetDocumentChunksAsync(string userId, string documentId, CancellationToken cancellationToken)
    {
        var options = new SearchOptions
        {
            Size = 1000,
            Filter = $"{OwnerFilter(userId)} and documentId eq '{Escape(documentId)}'",
            OrderBy = { "chunkIndex asc" }
        };
        foreach (var field in SelectFields)
        {
            options.Select.Add(field);
        }

        var results = await searchClient.SearchAsync<SearchDocument>("*", options, cancellationToken);
        var hits = new List<SearchHit>();
        await foreach (var result in results.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            hits.Add(ToHit(result.Document));
        }

        return hits;
    }

    public async Task IndexChunksAsync(UserDocument document, IReadOnlyList<DocumentChunk> chunks,
        IReadOnlyList<ReadOnlyMemory<float>> vectors, CancellationToken cancellationToken)
    {
        var items = chunks.Select((content, index) => new SearchDocument
        {
            ["id"] = ChunkId(document.Id, index),
            ["documentId"] = document.Id,
            ["ownerId"] = document.UserId,
            ["chunkIndex"] = index,
            ["fileName"] = document.FileName,
            ["sourceType"] = document.SourceType,
            ["sourceUrl"] = document.SourceUrl,
            ["content"] = content.Content,
            ["sectionPath"] = content.Section,
            ["sectionPaths"] = content.SectionPaths,
            ["pageNumber"] = content.PageNumber,
            ["contentVector"] = vectors[index].ToArray()
        }).ToList();

        foreach (var batch in items.Chunk(UploadBatchSize))
        {
            await searchClient.MergeOrUploadDocumentsAsync(batch,
                new IndexDocumentsOptions { ThrowOnAnyError = true }, cancellationToken);
        }
    }

    public async Task DeleteDocumentChunksAsync(string userId, string documentId, CancellationToken cancellationToken)
    {
        var options = new SearchOptions
        {
            Size = 1000,
            Filter = $"{OwnerFilter(userId)} and documentId eq '{Escape(documentId)}'",
            Select = { "id" }
        };
        var results = await searchClient.SearchAsync<SearchDocument>("*", options, cancellationToken);
        var ids = new List<string>();
        await foreach (var result in results.Value.GetResultsAsync().WithCancellation(cancellationToken))
        {
            ids.Add(GetString(result.Document, "id"));
        }

        foreach (var batch in ids.Chunk(UploadBatchSize))
        {
            await searchClient.DeleteDocumentsAsync("id", batch, new IndexDocumentsOptions { ThrowOnAnyError = true }, cancellationToken);
        }
    }

    public static string OwnerFilter(string userId) => $"ownerId eq '{Escape(userId)}'";

    public static string ScopeFilter(string userId, IReadOnlyList<string> documentIds, string? section = null, int? pageNumber = null)
    {
        var documents = documentIds.Count == 0 ? "false"
            : "(" + string.Join(" or ", documentIds.Select(id => $"documentId eq '{Escape(id)}'")) + ")";
        var filter = $"{OwnerFilter(userId)} and {documents}";
        if (section is not null) filter += $" and sectionPaths/any(s: s eq '{Escape(section)}')";
        if (pageNumber is not null) filter += $" and pageNumber eq {pageNumber.Value}";
        return filter;
    }

    public static string ChunkId(string documentId, int index) => $"{documentId}-c{index}";

    private static SearchHit ToHit(SearchDocument document) => new(
        GetString(document, "id"),
        GetString(document, "documentId"),
        GetString(document, "fileName"),
        NullIfEmpty(GetString(document, "sourceUrl")),
        GetString(document, "content"),
        GetString(document, "sourceType"),
        document.TryGetValue("chunkIndex", out var index) && index is int number ? number : 0,
        NullIfEmpty(GetString(document, "sectionPath")),
        document.TryGetValue("pageNumber", out var page) && page is int pageNumber ? pageNumber : null);

    private static string Escape(string value) => value.Replace("'", "''");

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static string GetString(SearchDocument document, string field) =>
        document.TryGetValue(field, out var value) && value is string text ? text : "";
}

/// <summary>Creates the index, or updates it with added fields, before the API starts serving requests.</summary>
public sealed class KnowledgeIndexInitializer(
    SearchIndexClient indexClient,
    IOptions<RagOptions> options,
    ILogger<KnowledgeIndexInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        try
        {
            await indexClient.CreateOrUpdateIndexAsync(
                KnowledgeIndex.CreateDefinition(settings.IndexName, settings.EmbeddingDimensions), cancellationToken: cancellationToken);
            logger.LogInformation("Search index {IndexName} is ready.", settings.IndexName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogCritical(exception,
                "Could not create or update search index {IndexName}. The API identity needs Search Service Contributor, and an " +
                "existing index with an incompatible schema (for example other vector dimensions) needs a new Rag:IndexName.",
                settings.IndexName);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
