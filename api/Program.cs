using Azure.Core;
using Azure.Identity;
using Azure.Search.Documents;
using Chatbot.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Identity.Web;
using Microsoft.IdentityModel.Logging;
using OpenAI.Responses;
using System.ClientModel.Primitives;
using Microsoft.Extensions.Options;

#pragma warning disable OPENAI001 // The Responses API SDK is marked experimental.

var builder = WebApplication.CreateBuilder(args);

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
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddMicrosoftIdentityWebApi(builder.Configuration);
    builder.Services.AddAuthorization(options =>
        options.AddPolicy("AccessAsUser", policy => policy.RequireAuthenticatedUser().RequireScope("access_as_user")));
}
else
{
    builder.Services.AddAuthorization();
}

builder.Services.AddControllers(options =>
{
    if (!useMockAuthentication)
    {
        options.Filters.Add(new AuthorizeFilter("AccessAsUser"));
    }
});

if (builder.Configuration.GetValue<bool>("UseMockResponses"))
{
    builder.Services.AddSingleton<IChatService, MockChatService>();
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
    builder.Services.AddSingleton<TokenCredential>(_ => builder.Environment.IsDevelopment()
        ? new DefaultAzureCredential()
        : new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned));
    builder.Services.AddSingleton(services =>
    {
        var options = services.GetRequiredService<IOptions<RagOptions>>().Value;
        return new SearchClient(new Uri(options.SearchEndpoint), options.IndexName,
            services.GetRequiredService<TokenCredential>());
    });
    builder.Services.AddSingleton(services =>
    {
        var options = services.GetRequiredService<IOptions<RagOptions>>().Value;
        return new ResponsesClient(
            new BearerTokenPolicy(services.GetRequiredService<TokenCredential>(), "https://ai.azure.com/.default"),
            new ResponsesClientOptions { Endpoint = new Uri($"{options.FoundryEndpoint.TrimEnd('/')}/openai/v1/") });
    });
    builder.Services.AddSingleton<IChatService, RagChatService>();
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

app.Run();

public partial class Program;
