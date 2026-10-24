using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core.Pipeline;
using Azure.Search.Documents;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenAI.Responses;
using Xunit;

namespace Chatbot.Api.Tests;

public sealed class MessagesTests
{
    [Fact]
    public async Task RagStreamsGroundedAnswerAndSourcesUsingResponsesApi()
    {
        using var factory = new ChatFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = "What about housing?" });
        var stream = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Searching parliamentary questions", stream);
        Assert.Contains("Grounded answer [1]", stream);
        Assert.Contains("Sources", stream);
        Assert.Contains("Housing", stream);
        Assert.EndsWith("data: [DONE]\n\n", stream);
        Assert.Equal("/openai/v1/responses", factory.Foundry.Path);
        using var request = JsonDocument.Parse(Assert.IsType<string>(factory.Foundry.Body));
        Assert.Equal("gpt-6.1-sol", request.RootElement.GetProperty("model").GetString());
        Assert.False(request.RootElement.GetProperty("store").GetBoolean());
        Assert.True(request.RootElement.GetProperty("stream").GetBoolean());
        Assert.Contains("Indexed evidence", request.RootElement.GetProperty("input").ToString());
        using var search = JsonDocument.Parse(Assert.IsType<string>(factory.Search.Body));
        Assert.Equal("semantic", search.RootElement.GetProperty("queryType").GetString());
        Assert.Equal("contentVector", search.RootElement.GetProperty("vectorQueries")[0].GetProperty("fields").GetString());
        Assert.Equal(5, search.RootElement.GetProperty("top").GetInt32());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task InvalidMessagesReturnBadRequest(string? message)
    {
        using var factory = new ChatFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(factory.Search.Body);
    }

    [Fact]
    public async Task OversizedMessageReturnsBadRequest()
    {
        using var factory = new ChatFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = new string('a', 8001) });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task NoSourcesDoesNotCallFoundry()
    {
        using var factory = new ChatFactory { EmptySearch = true };
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing" });
        var stream = await response.Content.ReadAsStringAsync();
        Assert.Contains("find relevant sources", stream);
        Assert.EndsWith("data: [DONE]\n\n", stream);
        Assert.Null(factory.Foundry.Body);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task UpstreamFailureEmitsErrorWithoutDone(bool searchFails, bool foundryFails)
    {
        using var factory = new ChatFactory { SearchFails = searchFails, FoundryFails = foundryFails };
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing" });
        var stream = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"error\":", stream);
        Assert.DoesNotContain("[DONE]", stream);
        Assert.DoesNotContain("private upstream detail", stream);
    }

    [Fact]
    public async Task IncompleteResponseIsNotReportedAsSuccess()
    {
        using var factory = new ChatFactory { IncompleteResponse = true };
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing" });
        var stream = await response.Content.ReadAsStringAsync();
        Assert.Contains("Grounded answer [1]", stream);
        Assert.Contains("\"error\":", stream);
        Assert.DoesNotContain("[DONE]", stream);
        Assert.DoesNotContain("### Sources", stream);
    }

    [Fact]
    public async Task CompletedResponseWithoutTextIsNotReportedAsSuccess()
    {
        using var factory = new ChatFactory { EmptyResponse = true };
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing" });
        var stream = await response.Content.ReadAsStringAsync();
        Assert.NotNull(factory.Foundry.Body);
        Assert.Contains("\"error\":", stream);
        Assert.DoesNotContain("[DONE]", stream);
    }

    [Fact]
    public async Task RealAuthenticationStillRequiresBearerToken()
    {
        using var factory = new ChatFactory { MockAuthentication = false };
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(factory.Search.Body);
    }

    [Fact]
    public async Task MockResponseDoesNotUseAzure()
    {
        using var factory = new ChatFactory { MockResponses = true };
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = "hello" });
        var stream = await response.Content.ReadAsStringAsync();
        Assert.Contains("from the mock API.", stream);
        Assert.EndsWith("data: [DONE]\n\n", stream);
        Assert.Null(factory.Search.Body);
        Assert.Null(factory.Foundry.Body);
    }

    private sealed class ChatFactory : WebApplicationFactory<Program>
    {
        public bool MockAuthentication { get; init; } = true;
        public bool MockResponses { get; init; }
        public bool EmptySearch { get; init; }
        public bool SearchFails { get; init; }
        public bool FoundryFails { get; init; }
        public bool IncompleteResponse { get; init; }
        public bool EmptyResponse { get; init; }
        public RecordingHandler Search { get; } = new();
        public RecordingHandler Foundry { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("UseMockAuthentication", MockAuthentication.ToString());
            builder.UseSetting("UseMockResponses", MockResponses.ToString());
            builder.UseSetting("Logging:EnablePiiLogging", "false");
            builder.ConfigureServices(services =>
            {
                Search.Status = SearchFails ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
                Search.ResponseBody = EmptySearch ? """{"value":[]}""" : """
                    {"value":[{"@search.score":1,"@search.rerankerScore":3,"id":"doc1",
                    "content":"Indexed evidence about housing.","onderwerp":"Housing","documentNummer":"2026Z001"}]}
                    """;
                Foundry.Status = FoundryFails ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
                Foundry.ContentType = "text/event-stream";
                Foundry.ResponseBody = EmptyResponse ? "" : """
                    event: response.output_text.delta
                    data: {"type":"response.output_text.delta","sequence_number":1,"item_id":"msg1","output_index":0,"content_index":0,"delta":"Grounded answer [1]"}


                    """;
                if (!IncompleteResponse)
                {
                    Foundry.ResponseBody += """
                        event: response.completed
                        data: {"type":"response.completed","sequence_number":2,"response":{"id":"resp1","object":"response","created_at":1,"status":"completed","model":"gpt-6.1-sol","output":[]}}


                        """;
                }

                services.AddSingleton(new SearchClient(
                    new Uri("https://search.test"), "kamervragen-openai", new AzureKeyCredential("test"),
                    new SearchClientOptions { Transport = new HttpClientTransport(new HttpClient(Search)) }));
                services.AddSingleton(new ResponsesClient(new ApiKeyCredential("test"),
                    new ResponsesClientOptions
                    {
                        Endpoint = new Uri("https://foundry.test/openai/v1/"),
                        Transport = new HttpClientPipelineTransport(new HttpClient(Foundry))
                    }));
            });
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Path { get; private set; }
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string ContentType { get; set; } = "application/json";
        public string ResponseBody { get; set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Path = request.RequestUri?.AbsolutePath;
            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Status == HttpStatusCode.OK
                    ? ResponseBody : """{"error":{"code":"Forbidden","message":"private upstream detail"}}""",
                    Encoding.UTF8, ContentType)
            };
        }
    }
}
