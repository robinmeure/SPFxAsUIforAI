using Azure.Core;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Chatbot.Api.Services;
using Chatbot.Api.Services.Agent;
using Chatbot.Api.Services.Graph;
using Chatbot.Api.Services.Ingestion;
using Chatbot.Api.Services.Storage;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Azure.Cosmos;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
using OpenAI;
using OpenAI.Embeddings;
using OpenAI.Responses;
using System.ClientModel.Primitives;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

#pragma warning disable OPENAI001 // The Responses API SDK is marked experimental.

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource(Telemetry.SourceName));

builder.Services.AddCors(options =>
{
    options.AddPolicy("SharePointAndTeams", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

var useMockAuthentication = builder.Configuration.GetValue<bool>("UseMockAuthentication");
if (!useMockAuthentication)
{
    // Token acquisition enables the On-Behalf-Of flow to Microsoft Graph (SharePoint files). It needs
    // AzureAd:ClientSecret, supplied as an App Service Key Vault reference, never stored in source.
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApi(builder.Configuration)
        .EnableTokenAcquisitionToCallDownstreamApi()
        .AddInMemoryTokenCaches();
    builder.Services.AddAuthorization(options =>
        options.AddPolicy("AccessAsUser", policy => policy.RequireAuthenticatedUser().RequireScope("access_as_user")));
    builder.Services.AddOptions<GraphOptions>().BindConfiguration("Graph");
    builder.Services.AddHttpClient<IGraphFiles, GraphFiles>();
}
else
{
    builder.Services.AddAuthorization();
    builder.Services.AddSingleton<IGraphFiles, UnavailableGraphFiles>();
}

builder.Services.AddControllers(options =>
{
    if (!useMockAuthentication)
    {
        options.Filters.Add(new AuthorizeFilter("AccessAsUser"));
    }
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ChatDocumentSets>();
builder.Services.AddSingleton(services => new SkillCatalog(
    Path.Combine(AppContext.BaseDirectory, "skills"), services.GetRequiredService<ILogger<SkillCatalog>>()));
builder.Services.AddOptions<IngestionOptions>().BindConfiguration("Ingestion");
builder.Services.AddSingleton<IngestionQueue>();
builder.Services.AddHostedService<IngestionWorker>();
builder.Services.AddScoped<DocumentCleanup>();
builder.Services.AddHostedService<DocumentCleanupWorker>();

var useMockResponses = builder.Configuration.GetValue<bool>("UseMockResponses");
if (!useMockResponses)
{
    builder.Services.AddSingleton<TokenCredential>(services => new CachedTokenCredential(builder.Environment.IsDevelopment()
        ? new DefaultAzureCredential()
        : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned), services.GetRequiredService<TimeProvider>()));
}

var cosmosEndpoint = builder.Configuration["Cosmos:Endpoint"];
if (!string.IsNullOrWhiteSpace(cosmosEndpoint) && !useMockResponses)
{
    builder.Services.AddOptions<CosmosOptions>().BindConfiguration("Cosmos");
    builder.Services.AddSingleton(services => new CosmosClient(cosmosEndpoint, services.GetRequiredService<TokenCredential>(),
        new CosmosClientOptions
        {
            ApplicationName = "chatbot-api",
            UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        }));
    builder.Services.AddSingleton<IChatStore, CosmosChatStore>();
    builder.Services.AddSingleton<IDocumentRegistry, CosmosDocumentRegistry>();
}
else
{
    builder.Services.AddSingleton<IChatStore, InMemoryChatStore>();
    builder.Services.AddSingleton<IDocumentRegistry, InMemoryDocumentRegistry>();
}

if (useMockResponses)
{
    builder.Services.AddSingleton<IChatService, MockChatService>();
    builder.Services.AddSingleton<IDocumentIngestor, MockDocumentIngestor>();
}
else
{
    builder.Services.AddOptions<RagOptions>()
        .BindConfiguration("Rag")
        .ValidateDataAnnotations()
        .Validate(options => Uri.TryCreate(options.SearchEndpoint, UriKind.Absolute, out var search)
            && search.Scheme == Uri.UriSchemeHttps
            && Uri.TryCreate(options.FoundryEndpoint, UriKind.Absolute, out var foundry)
            && foundry.Scheme == Uri.UriSchemeHttps, "RAG endpoints must be absolute HTTPS URLs.")
        .ValidateOnStart();
    builder.Services.AddSingleton(services => new SearchIndexClient(
        new Uri(services.GetRequiredService<IOptions<RagOptions>>().Value.SearchEndpoint),
        services.GetRequiredService<TokenCredential>()));
    builder.Services.AddSingleton(services =>
    {
        var options = services.GetRequiredService<IOptions<RagOptions>>().Value;
        return new SearchClient(new Uri(options.SearchEndpoint), options.IndexName,
            services.GetRequiredService<TokenCredential>());
    });
    builder.Services.AddHostedService<KnowledgeIndexInitializer>();
    builder.Services.AddSingleton(services =>
    {
        var options = services.GetRequiredService<IOptions<RagOptions>>().Value;
        return new ResponsesClient(
            new BearerTokenPolicy(services.GetRequiredService<TokenCredential>(), "https://ai.azure.com/.default"),
            new ResponsesClientOptions { Endpoint = FoundryV1Endpoint(options) });
    });
    builder.Services.AddSingleton(services =>
    {
        var options = services.GetRequiredService<IOptions<RagOptions>>().Value;
        return new EmbeddingClient(options.EmbeddingDeployment,
            new BearerTokenPolicy(services.GetRequiredService<TokenCredential>(), "https://ai.azure.com/.default"),
            new OpenAIClientOptions { Endpoint = FoundryV1Endpoint(options) });
    });
    // The Foundry (AIServices) account also serves Document Intelligence; it needs Cognitive Services User.
    builder.Services.AddSingleton(services => new DocumentIntelligenceClient(
        new Uri(services.GetRequiredService<IOptions<RagOptions>>().Value.FoundryEndpoint),
        services.GetRequiredService<TokenCredential>()));
    builder.Services.AddSingleton<ITextExtractor, DocumentIntelligenceTextExtractor>();
    builder.Services.AddSingleton<IEmbeddingGenerator, OpenAIEmbeddingGenerator>();
    builder.Services.AddSingleton<KnowledgeIndex>();
    builder.Services.AddSingleton<DocumentRetrieval>();
    builder.Services.AddSingleton<IDocumentIngestor, DocumentIngestor>();

    builder.Services.AddScoped<IAgentTool, ListMyDocumentsTool>();
    builder.Services.AddScoped<IAgentTool, CreateDocumentTool>();
    builder.Services.AddScoped<IAgentTool, LoadSkillTool>();
    builder.Services.AddScoped<IAgentTool, ReadSkillResourceTool>();
    builder.Services.AddScoped<IChatService, AgentChatService>();
}

var app = builder.Build();
IdentityModelEventSource.ShowPII = app.Environment.IsDevelopment()
    && app.Configuration.GetValue<bool>("Logging:EnablePiiLogging");
if (IdentityModelEventSource.ShowPII)
{
    app.Logger.LogWarning("IdentityModel PII logging is enabled. Authentication logs may contain sensitive personal data. Disable Logging:EnablePiiLogging after troubleshooting.");
}
app.UseCors("SharePointAndTeams");
if (!useMockAuthentication)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.MapControllers();
app.MapDefaultEndpoints();

app.Run();

static Uri FoundryV1Endpoint(RagOptions options) => new($"{options.FoundryEndpoint.TrimEnd('/')}/openai/v1/");

public partial class Program;
