using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure;
using Azure.Core.Pipeline;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Chatbot.Api.Services.Ingestion;
using Chatbot.Api.Services.Agent;
using Chatbot.Api.Services.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenAI.Responses;
using Xunit;

namespace Chatbot.Api.Tests;

public sealed partial class MessagesTests
{
    private const string SkillCall = """
        event: response.output_item.done
        data: {"type":"response.output_item.done","sequence_number":1,"output_index":0,"item":{"type":"function_call","id":"fc1","call_id":"call1","name":"load_skill","arguments":"{\"name\":\"extract-and-generate\"}","status":"completed"}}


        """;

    private static readonly string AnswerText = StructuredText("Grounded answer [1]", ["What can you explain next?"]);

    private const string Completed = """
        event: response.completed
        data: {"type":"response.completed","sequence_number":9,"response":{"id":"resp1","object":"response","created_at":1,"status":"completed","model":"gpt-6.1-sol","output":[]}}


        """;

    [Fact]
    public async Task ApplicationSearchesBeforeAnswerAndPersistsPassageCitations()
    {
        using var factory = new ChatFactory { SeedDocuments = true };
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = "What about housing?", documentIds = new[] { "doc1" } });
        var stream = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("data: {\"chatId\":", stream);
        Assert.Contains("Searched thread: 1 passages", stream);
        Assert.Contains("Grounded answer [1]", stream);
        Assert.Contains("\"citations\":[", stream);
        Assert.Contains("Housing", stream);
        Assert.EndsWith("data: [DONE]\n\n", stream);

        Assert.Single(factory.Foundry.RoutedBodies);
        using var plan = JsonDocument.Parse(factory.Foundry.RoutedBodies[0]);
        Assert.Equal("low", plan.RootElement.GetProperty("reasoning").GetProperty("effort").GetString());
        Assert.Single(factory.Foundry.Bodies);
        using var first = JsonDocument.Parse(factory.Foundry.Bodies[0]);
        Assert.Equal("gpt-6.1-sol", first.RootElement.GetProperty("model").GetString());
        Assert.False(first.RootElement.TryGetProperty("reasoning", out _));
        Assert.False(first.RootElement.GetProperty("store").GetBoolean());
        Assert.DoesNotContain(first.RootElement.GetProperty("tools").EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "search_documents");
        Assert.Contains("extract-and-generate", first.RootElement.GetProperty("instructions").GetString());
        Assert.Contains("Indexed evidence about housing.", first.RootElement.GetProperty("input").ToString());

        using var search = JsonDocument.Parse(Assert.Single(factory.Search.Bodies));
        Assert.Equal("semantic", search.RootElement.GetProperty("queryType").GetString());
        var vectorQuery = search.RootElement.GetProperty("vectorQueries")[0];
        Assert.Equal("vector", vectorQuery.GetProperty("kind").GetString());
        Assert.Equal("contentVector", vectorQuery.GetProperty("fields").GetString());
        Assert.Equal(FakeEmbeddings.Dimensions, vectorQuery.GetProperty("vector").GetArrayLength());
        Assert.Equal("ownerId eq 'local-development-user' and (documentId eq 'doc1')", search.RootElement.GetProperty("filter").GetString());
        Assert.Equal("preFilter", search.RootElement.GetProperty("vectorFilterMode").GetString());

        var id = ChatIdRegex().Match(stream).Groups[1].Value;
        var details = await client.GetFromJsonAsync<JsonElement>($"/api/chats/{id}");
        Assert.Equal("doc1", Assert.Single(details.GetProperty("documentIds").EnumerateArray()).GetString());
        var citation = Assert.Single(details.GetProperty("messages")[1].GetProperty("citations").EnumerateArray());
        Assert.Equal("doc1-c0", citation.GetProperty("chunkId").GetString());
        Assert.Equal("Indexed evidence about housing.", citation.GetProperty("quote").GetString());
        Assert.Equal("Report > Housing", citation.GetProperty("section").GetString());
        Assert.Equal(2, citation.GetProperty("pageNumber").GetInt32());
        Assert.Equal("sharepoint", citation.GetProperty("sourceType").GetString());
    }

    [Fact]
    public async Task StartupCreatesTheDocumentsIndex()
    {
        using var factory = new ChatFactory();
        using var client = factory.CreateClient();

        using var definition = JsonDocument.Parse(Assert.Single(factory.SearchAdmin.Bodies));
        var root = definition.RootElement;
        Assert.Equal("my-documents", root.GetProperty("name").GetString());
        var fields = root.GetProperty("fields").EnumerateArray().ToDictionary(field => field.GetProperty("name").GetString()!);
        Assert.True(fields["id"].GetProperty("key").GetBoolean());
        Assert.True(fields["ownerId"].GetProperty("filterable").GetBoolean());
        Assert.True(fields["documentId"].GetProperty("filterable").GetBoolean());
        Assert.True(fields["chunkIndex"].GetProperty("sortable").GetBoolean());
        Assert.Equal(1024, fields["contentVector"].GetProperty("dimensions").GetInt32());
        Assert.Equal("semantic", root.GetProperty("semantic").GetProperty("configurations")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void ApiDoesNotStartWhenTheIndexCannotBeCreated()
    {
        using var factory = new ChatFactory { SearchAdminFails = true };
        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task UncitedSourcesAreNotListed()
    {
        using var factory = new ChatFactory { SeedDocuments = true, FoundryResponses = [AnswerText.Replace(" [1]", "") + Completed] };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing", documentIds = new[] { "doc1" } })).Content.ReadAsStringAsync();
        Assert.Contains("Grounded answer", stream);
        Assert.DoesNotContain("### Sources", stream);
        Assert.EndsWith("data: [DONE]\n\n", stream);
        var id = ChatIdRegex().Match(stream).Groups[1].Value;
        var details = await client.GetFromJsonAsync<JsonElement>($"/api/chats/{id}");
        Assert.Empty(details.GetProperty("messages")[1].GetProperty("citations").EnumerateArray());
    }

    [Fact]
    public async Task FollowUpMessageSendsStoredHistoryWithoutAppendix()
    {
        using var factory = new ChatFactory { SeedDocuments = true, FoundryResponses = [AnswerText + Completed, AnswerText + Completed] };
        using var client = factory.CreateClient();
        var firstStream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "First question", documentIds = new[] { "doc1" } })).Content.ReadAsStringAsync();
        var chatId = ChatIdRegex().Match(firstStream).Groups[1].Value;

        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = "Follow-up", chatId });
        var stream = await response.Content.ReadAsStringAsync();

        Assert.Contains($"\"chatId\":\"{chatId}\"", stream);
        var input = JsonDocument.Parse(factory.Foundry.Bodies[^1]).RootElement.GetProperty("input").ToString();
        Assert.Contains("First question", input);
        Assert.Contains("Grounded answer", input);
        Assert.DoesNotContain("Grounded answer [1]", input);
        Assert.DoesNotContain("### Sources", input);
        Assert.Contains("Follow-up", input);

        var chat = await client.GetFromJsonAsync<JsonElement>($"/api/chats/{chatId}");
        Assert.Equal("First question", chat.GetProperty("title").GetString());
        Assert.Equal(4, chat.GetProperty("messages").GetArrayLength());
        Assert.Equal(2, factory.Search.Bodies.Count);
        Assert.All(factory.Search.Bodies, body => Assert.Contains("documentId eq 'doc1'", body));
    }

    [Fact]
    public async Task SkillsCanBeListedAndASelectedSkillIsAppliedAndStored()
    {
        using var factory = new ChatFactory();
        using var client = factory.CreateClient();
        var skills = await client.GetFromJsonAsync<JsonElement>("/api/skills");
        var skill = Assert.Single(skills.EnumerateArray(), item => item.GetProperty("name").GetString() == "extract-and-generate");
        Assert.False(string.IsNullOrWhiteSpace(skill.GetProperty("description").GetString()));

        var stream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "Turn this into a table", skill = "extract-and-generate" })).Content.ReadAsStringAsync();
        Assert.EndsWith("data: [DONE]\n\n", stream);
        var instructions = JsonDocument.Parse(Assert.Single(factory.Foundry.Bodies)).RootElement.GetProperty("instructions").GetString();
        Assert.Contains("The user selected the skill 'extract-and-generate'", instructions);
        Assert.Contains("<skill-instructions>", instructions);
        Assert.Contains("# Extract data and generate a document", instructions);

        var chatId = ChatIdRegex().Match(stream).Groups[1].Value;
        var messages = (await client.GetFromJsonAsync<JsonElement>($"/api/chats/{chatId}")).GetProperty("messages");
        Assert.Equal("extract-and-generate", messages[0].GetProperty("skill").GetString());
        Assert.Equal(JsonValueKind.Null, messages[1].GetProperty("skill").ValueKind);
    }

    [Theory]
    [InlineData("not-a-skill")]
    [InlineData("Bad Skill")]
    public async Task UnknownOrMalformedSkillIsRejectedBeforeAnyWork(string skill)
    {
        using var factory = new ChatFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = "hi", skill });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Foundry.Bodies);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/chats")).EnumerateArray());
    }

    [Fact]
    public async Task UnknownChatReturnsNotFound()
    {
        using var factory = new ChatFactory();
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/messages/stream", new { message = "hi", chatId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(factory.Foundry.Bodies);
    }

    [Fact]
    public async Task SectionIntentIsEnforcedBySearchAndReportedToTheClient()
    {
        using var factory = new ChatFactory
        {
            SeedDocuments = true,
            Plan = new("rent exceptions", "section", ["doc1"], "Report > Housing", 2, null)
        };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "What are the exceptions on page 2 of Housing?", documentIds = new[] { "doc1", "doc2" } })).Content.ReadAsStringAsync();
        Assert.EndsWith("data: [DONE]\n\n", stream);
        var search = JsonDocument.Parse(Assert.Single(factory.Search.Bodies)).RootElement;
        Assert.Equal("rent exceptions", search.GetProperty("search").GetString());
        Assert.Equal("ownerId eq 'local-development-user' and (documentId eq 'doc1') and sectionPaths/any(s: s eq 'Report > Housing') and pageNumber eq 2",
            search.GetProperty("filter").GetString());
        Assert.Contains("\"scope\":\"section\"", stream);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoAttachmentsNeverSearchesTheUsersOtherDocuments(bool explicitEmptySet)
    {
        var registry = new Mock<IDocumentRegistry>(MockBehavior.Strict);
        registry.Setup(store => store.ReadCleanupPageAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentCleanupPage([], null));
        using var factory = new ChatFactory { SeedDocuments = true, RegistryOverride = registry.Object };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "What are my policies?", documentIds = explicitEmptySet ? Array.Empty<string>() : null })).Content.ReadAsStringAsync();
        Assert.Empty(factory.Search.Bodies);
        Assert.Empty(factory.Search.RoutedBodies);
        Assert.Empty(factory.Foundry.RoutedBodies);
        Assert.Contains("No documents are attached", stream);
        Assert.DoesNotContain("Planning document search", stream);
        Assert.DoesNotContain("Reviewing your question", stream);
        Assert.DoesNotContain("Searched ", stream);
        registry.Verify(store => store.ListAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        registry.Verify(store => store.GetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FactualFollowUpCanReuseValidatedEvidenceWithoutSearchOrAnExtraAnswerCall()
    {
        using var factory = new ChatFactory { SeedDocuments = true };
        using var client = factory.CreateClient();
        var firstStream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "What is the housing policy?", documentIds = new[] { "doc1" } })).Content.ReadAsStringAsync();
        var chatId = ChatIdRegex().Match(firstStream).Groups[1].Value;
        factory.Plan = new("Which policy was mentioned?", "reuse", [], null, null, null);

        var stream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "Which policy was mentioned?", chatId })).Content.ReadAsStringAsync();
        Assert.Contains("Reusing earlier source passages", stream);
        Assert.DoesNotContain("Searched ", stream);
        Assert.Single(factory.Search.Bodies);
        Assert.Single(factory.Search.RoutedBodies);
        Assert.Equal(2, factory.Foundry.RoutedBodies.Count);
        Assert.Equal(2, factory.Foundry.Bodies.Count);
        var input = JsonDocument.Parse(factory.Foundry.Bodies[^1]).RootElement.GetProperty("input").ToString();
        Assert.Contains("Indexed evidence about housing.", input);
        var chat = await client.GetFromJsonAsync<JsonElement>($"/api/chats/{chatId}");
        var last = chat.GetProperty("messages")[3];
        Assert.Equal("reuse", last.GetProperty("retrieval").GetProperty("method").GetString());
        var citation = Assert.Single(last.GetProperty("citations").EnumerateArray());
        Assert.Equal(1, citation.GetProperty("number").GetInt32());
        Assert.Equal("reuse", citation.GetProperty("retrievalMethod").GetString());
    }

    [Fact]
    public async Task ConversationWithAttachmentsSkipsHybridSearch()
    {
        using var factory = new ChatFactory
        {
            SeedDocuments = true, Plan = new("Thank you", "conversation", [], null, null, null)
        };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "Thank you", documentIds = new[] { "doc1" } })).Content.ReadAsStringAsync();
        Assert.EndsWith("data: [DONE]\n\n", stream);
        Assert.Empty(factory.Search.Bodies);
        Assert.Single(factory.Search.RoutedBodies);
        Assert.DoesNotContain("Searched ", stream);
        Assert.Contains("\"method\":\"none\"", stream);
    }

    [Fact]
    public async Task ReuseWithoutValidatedEvidenceFailsBeforeAnswerGeneration()
    {
        using var factory = new ChatFactory
        {
            SeedDocuments = true, Plan = new("What is the policy?", "reuse", [], null, null, null)
        };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "What is the policy?", documentIds = new[] { "doc1" } })).Content.ReadAsStringAsync();
        Assert.Contains("\"error\":", stream);
        Assert.DoesNotContain("[DONE]", stream);
        Assert.Empty(factory.Search.Bodies);
        Assert.Empty(factory.Foundry.Bodies);
    }

    [Fact]
    public async Task StructuredAnswerStreamsSuggestionsWithoutAnotherModelCall()
    {
        var response = JsonSerializer.Serialize(new
        {
            answer = "A grounded answer.\n\nIt supports a follow-up.",
            followUpQuestions = new[] { "What should I explore next?", "Can you give an example?" },
            offerFollowUps = true
        });
        var deltas = string.Concat(response.Select(character => TextDelta(character.ToString())));
        using var factory = new ChatFactory { FoundryResponses = [deltas + Completed] };
        using var client = factory.CreateClient();

        var stream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "Explain this topic." })).Content.ReadAsStringAsync();

        Assert.EndsWith("data: [DONE]\n\n", stream);
        Assert.Contains("\"followUpQuestions\":[\"What should I explore next?\",\"Can you give an example?\"]", stream);
        Assert.DoesNotContain("offerFollowUps", stream);
        Assert.Single(factory.Foundry.Bodies);
        var request = JsonDocument.Parse(factory.Foundry.Bodies[0]).RootElement;
        var format = request.GetProperty("text").GetProperty("format");
        Assert.Equal("json_schema", format.GetProperty("type").GetString());
        Assert.True(format.GetProperty("strict").GetBoolean());
        Assert.DoesNotContain(request.GetProperty("tools").EnumerateArray(),
            tool => tool.GetProperty("name").GetString() == "suggest_follow_ups");
        var chatId = ChatIdRegex().Match(stream).Groups[1].Value;
        var chat = await client.GetFromJsonAsync<JsonElement>($"/api/chats/{chatId}");
        Assert.Equal("A grounded answer.\n\nIt supports a follow-up.", chat.GetProperty("messages")[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task SuggestionsFollowCompletedToolWorkWithoutAnotherCall()
    {
        using var factory = new ChatFactory
        {
            FoundryResponses = [SkillCall + Completed,
                StructuredText("Grounded answer [1]", ["Compare the exceptions.", "Summarize this as a checklist."]) + Completed]
        };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "Explain this." })).Content.ReadAsStringAsync();
        Assert.Contains("\"followUpQuestions\":[\"Compare the exceptions.\",\"Summarize this as a checklist.\"]", stream);
        Assert.EndsWith("data: [DONE]\n\n", stream);
        Assert.Equal(2, factory.Foundry.Bodies.Count);
        Assert.Empty(factory.Foundry.RoutedBodies);
        var chatId = ChatIdRegex().Match(stream).Groups[1].Value;
        var chat = await client.GetFromJsonAsync<JsonElement>($"/api/chats/{chatId}");
        Assert.Equal(2, chat.GetProperty("messages")[1].GetProperty("followUpQuestions").GetArrayLength());
    }

    [Fact]
    public async Task ClosingExchangeHidesSuggestions()
    {
        using var factory = new ChatFactory
        {
            FoundryResponses = [StructuredText("Goodbye.", ["What can we discuss next?"], offerFollowUps: false) + Completed]
        };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "Thanks, goodbye." })).Content.ReadAsStringAsync();
        Assert.EndsWith("data: [DONE]\n\n", stream);
        Assert.Single(factory.Foundry.Bodies);
        Assert.Contains("\"followUpQuestions\":[]", stream);
        Assert.DoesNotContain("What can we discuss next?", stream);
    }

    [Fact]
    public async Task InvalidStructuredResponseFailsWithoutReportingSuccess()
    {
        using var factory = new ChatFactory
        {
            FoundryResponses = [TextDelta("""{"answer":"Incomplete","followUpQuestions":[],"offerFollowUps":true}""") + Completed]
        };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "Explain this." })).Content.ReadAsStringAsync();
        Assert.Contains("\"error\":", stream);
        Assert.DoesNotContain("[DONE]", stream);
    }

    [Fact]
    public async Task SectionCatalogIsReusedWithoutAddingPlannerRounds()
    {
        using var factory = new ChatFactory { SeedDocuments = true,
            Plan = new("rent exceptions", "section", ["doc1"], "Report > Housing", null, null) };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "Explain the Housing section.", documentIds = new[] { "doc1" } })).Content.ReadAsStringAsync();
        Assert.EndsWith("data: [DONE]\n\n", stream);
        var chatId = ChatIdRegex().Match(stream).Groups[1].Value;
        var followUp = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "Explain another Housing provision.", chatId })).Content.ReadAsStringAsync();
        Assert.EndsWith("data: [DONE]\n\n", followUp);
        Assert.Equal(2, factory.Foundry.RoutedBodies.Count);
        Assert.Single(factory.Search.RoutedBodies);
        Assert.Equal(2, factory.Search.Bodies.Count);
    }

    [Fact]
    public async Task ChangedDocumentVersionInvalidatesBothEvidenceAndSectionCatalog()
    {
        var document = new UserDocument
        {
            Id = "doc1", UserId = "local-development-user", FileName = "Housing.docx", SourceType = "upload",
            Status = DocumentStatus.Ready, UpdatedAt = DateTimeOffset.UtcNow
        };
        var registry = new Mock<IDocumentRegistry>(MockBehavior.Strict);
        registry.Setup(store => store.ReadCleanupPageAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentCleanupPage([], null));
        registry.Setup(store => store.ListAsync("local-development-user", It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<IReadOnlyList<UserDocument>>([document]));
        using var factory = new ChatFactory { RegistryOverride = registry.Object };
        using var client = factory.CreateClient();
        var firstStream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "Explain housing.", documentIds = new[] { "doc1" } })).Content.ReadAsStringAsync();
        var chatId = ChatIdRegex().Match(firstStream).Groups[1].Value;
        document = document with { UpdatedAt = document.UpdatedAt.AddMinutes(1) };
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "What changed?", chatId })).Content.ReadAsStringAsync();
        Assert.EndsWith("data: [DONE]\n\n", stream);
        Assert.Equal(2, factory.Search.RoutedBodies.Count);
        Assert.Equal(2, factory.Search.Bodies.Count);
        var input = JsonDocument.Parse(factory.Foundry.RoutedBodies[^1]).RootElement.GetProperty("input")[0];
        var plannerData = JsonDocument.Parse(input.GetProperty("content")[0].GetProperty("text").GetString()!).RootElement;
        Assert.Empty(plannerData.GetProperty("reusablePassages").EnumerateArray());
    }

    [Fact]
    public async Task OutOfScopeModelIntentStopsBeforeSearchOrAnswer()
    {
        using var factory = new ChatFactory
        {
            SeedDocuments = true, Plan = new("query", "document", ["doc2"], null, null, null)
        };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "Search another document", documentIds = new[] { "doc1" } })).Content.ReadAsStringAsync();
        Assert.Contains("\"error\":", stream);
        Assert.DoesNotContain("[DONE]", stream);
        Assert.Empty(factory.Search.Bodies);
        Assert.Empty(factory.Foundry.Bodies);
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
        Assert.Empty(factory.Foundry.Bodies);
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
    public async Task RetrievalFailureFailsTheTurnWithoutGeneratingAnUngroundedAnswer()
    {
        using var factory = new ChatFactory { SeedDocuments = true, SearchFails = true };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing", documentIds = new[] { "doc1" } })).Content.ReadAsStringAsync();
        Assert.Contains("\"error\":", stream);
        Assert.DoesNotContain("[DONE]", stream);
        Assert.DoesNotContain("private upstream detail", stream);
        Assert.Empty(factory.Foundry.Bodies);
    }

    [Fact]
    public async Task FoundryFailureEmitsErrorWithoutDone()
    {
        using var factory = new ChatFactory { FoundryFails = true };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing" })).Content.ReadAsStringAsync();
        Assert.Contains("\"error\":", stream);
        Assert.DoesNotContain("[DONE]", stream);
        Assert.DoesNotContain("private upstream detail", stream);
    }

    [Fact]
    public async Task IncompleteResponseIsNotReportedAsSuccess()
    {
        using var factory = new ChatFactory { FoundryResponses = [AnswerText] };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing" })).Content.ReadAsStringAsync();
        Assert.Contains("Grounded answer [1]", stream);
        Assert.Contains("\"error\":", stream);
        Assert.DoesNotContain("[DONE]", stream);
    }

    [Fact]
    public async Task CompletedResponseWithoutTextIsNotReportedAsSuccess()
    {
        using var factory = new ChatFactory { FoundryResponses = [Completed] };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing" })).Content.ReadAsStringAsync();
        Assert.Single(factory.Foundry.Bodies);
        Assert.Contains("\"error\":", stream);
        Assert.DoesNotContain("[DONE]", stream);
    }

    [Fact]
    public async Task ModelThatKeepsCallingToolsIsForcedToAnswerOnLastIteration()
    {
        using var factory = new ChatFactory { FoundryResponses = [.. Enumerable.Repeat(SkillCall + Completed, 7), AnswerText + Completed] };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing" })).Content.ReadAsStringAsync();
        Assert.EndsWith("data: [DONE]\n\n", stream);
        Assert.Equal(8, factory.Foundry.Bodies.Count);
        Assert.Equal("none", JsonDocument.Parse(factory.Foundry.Bodies[^1]).RootElement.GetProperty("tool_choice").GetString());
        Assert.Equal("auto", JsonDocument.Parse(factory.Foundry.Bodies[0]).RootElement.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task RealAuthenticationStillRequiresBearerToken()
    {
        using var factory = new ChatFactory { MockAuthentication = false };
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/messages/stream", new { message = "housing" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/chats")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/documents")).StatusCode);
        Assert.Empty(factory.Search.Bodies);
    }

    [Fact]
    public async Task MockResponseDoesNotUseAzure()
    {
        using var factory = new ChatFactory { MockResponses = true };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "hello" })).Content.ReadAsStringAsync();
        Assert.Contains("from the mock API.", stream);
        Assert.EndsWith("data: [DONE]\n\n", stream);
        Assert.Empty(factory.Search.Bodies);
        Assert.Empty(factory.Foundry.Bodies);
    }

    [GeneratedRegex("\"chatId\":\"([0-9a-f-]{36})\"")]
    private static partial Regex ChatIdRegex();

    private static string FunctionCall(string name, string arguments) =>
        "event: response.output_item.done\ndata: " + JsonSerializer.Serialize(new
        {
            type = "response.output_item.done", sequence_number = 2, output_index = 1,
            item = new { type = "function_call", id = "fc2", call_id = "call2", name, arguments, status = "completed" }
        }) + "\n\n";

    private static string TextDelta(string delta) =>
        "event: response.output_text.delta\ndata: " + JsonSerializer.Serialize(new
        {
            type = "response.output_text.delta", sequence_number = 1, item_id = "msg1",
            output_index = 0, content_index = 0, delta
        }) + "\n\n";

    private static string StructuredText(string answer, IReadOnlyList<string> followUpQuestions, bool offerFollowUps = true) =>
        TextDelta(JsonSerializer.Serialize(new { answer, followUpQuestions, offerFollowUps }));

    internal sealed class ChatFactory : WebApplicationFactory<Program>
    {
        public bool SeedDocuments { get; init; }
        public RetrievalPlan Plan { get; set; } = new("housing policy", "thread", [], null, null, null);
        public IDocumentRegistry? RegistryOverride { get; init; }
        public bool MockAuthentication { get; init; } = true;
        public bool MockResponses { get; init; }
        public bool SearchFails { get; init; }
        public bool SearchAdminFails { get; init; }
        public bool FoundryFails { get; init; }
        public string[] FoundryResponses { get; init; } = [AnswerText + Completed];
        public RecordingHandler Search { get; } = new();
        public RecordingHandler SearchAdmin { get; } = new();
        public RecordingHandler Foundry { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("UseMockAuthentication", MockAuthentication.ToString());
            builder.UseSetting("UseMockResponses", MockResponses.ToString());
            builder.UseSetting("Cosmos:Endpoint", "");
            builder.UseSetting("Logging:EnablePiiLogging", "false");
            builder.ConfigureServices(services =>
            {
                Search.Status = SearchFails ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
                Search.Responses.Enqueue("""
                    {"value":[{"@search.score":1,"@search.rerankerScore":3,"id":"doc1-c0","documentId":"doc1",
                    "content":"Indexed evidence about housing.","fileName":"Housing.docx","sourceType":"sharepoint",
                    "sourceUrl":"https://example.sharepoint.com/Housing.docx","chunkIndex":0,"sectionPath":"Report > Housing","pageNumber":2}]}
                    """);
                Search.Route = body => body.Contains("\"facets\"", StringComparison.Ordinal)
                    ? """{"@search.facets":{"sectionPaths":[{"value":"Report > Housing","count":1}]},"value":[]}""" : null;
                SearchAdmin.Status = SearchAdminFails ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
                SearchAdmin.Responses.Enqueue("""{"name":"my-documents","fields":[]}""");
                Foundry.Status = FoundryFails ? HttpStatusCode.Forbidden : HttpStatusCode.OK;
                Foundry.ContentType = "text/event-stream";
                Foundry.Route = body => body.Contains("\"name\":\"plan_retrieval\"", StringComparison.Ordinal)
                    ? FunctionCall("plan_retrieval", JsonSerializer.Serialize(Plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
                        + Completed : null;
                if (SeedDocuments)
                {
                    var registry = new InMemoryDocumentRegistry();
                    foreach (var id in new[] { "doc1", "doc2" })
                    {
                        registry.CreateAsync(new UserDocument
                        {
                            Id = id, UserId = "local-development-user", FileName = id == "doc1" ? "Housing.docx" : "Other.txt",
                            SourceType = id == "doc1" ? DocumentSource.SharePoint : DocumentSource.Upload, Status = DocumentStatus.Ready
                        }, CancellationToken.None).GetAwaiter().GetResult();
                    }
                    services.AddSingleton<IDocumentRegistry>(registry);
                }
                if (RegistryOverride is not null) services.AddSingleton(RegistryOverride);
                foreach (var body in FoundryResponses)
                {
                    Foundry.Responses.Enqueue(body);
                }

                services.AddSingleton(new SearchClient(
                    new Uri("https://search.test"), "my-documents", new AzureKeyCredential("test"),
                    new SearchClientOptions { Transport = new HttpClientTransport(new HttpClient(Search)) }));
                services.AddSingleton(new SearchIndexClient(
                    new Uri("https://search.test"), new AzureKeyCredential("test"),
                    new SearchClientOptions { Transport = new HttpClientTransport(new HttpClient(SearchAdmin)) }));
                services.AddSingleton<IEmbeddingGenerator, FakeEmbeddings>();
                services.AddSingleton(new ResponsesClient(new ApiKeyCredential("test"),
                    new ResponsesClientOptions
                    {
                        Endpoint = new Uri("https://foundry.test/openai/v1/"),
                        Transport = new HttpClientPipelineTransport(new HttpClient(Foundry))
                    }));
            });
        }
    }

    internal sealed class FakeEmbeddings : IEmbeddingGenerator
    {
        public const int Dimensions = 4;

        public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(IReadOnlyList<string> inputs, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(inputs.Select(_ => new ReadOnlyMemory<float>(new float[Dimensions])).ToList());
    }

    /// <summary>Records request bodies and replays queued responses; the last response repeats when the queue runs out.</summary>
    internal sealed class RecordingHandler : HttpMessageHandler
    {
        private string _last = "";

        public List<string> Bodies { get; } = [];
        public List<string> RoutedBodies { get; } = [];
        public Func<string, string?>? Route { get; set; }
        public Queue<string> Responses { get; } = new();
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string ContentType { get; set; } = "application/json";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            string response;
            lock (Bodies)
            {
                var routed = Route?.Invoke(body);
                if (routed is not null) { RoutedBodies.Add(body); response = routed; }
                else
                {
                    Bodies.Add(body);
                    response = Responses.TryDequeue(out var next) ? _last = next : _last;
                }
            }

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Status == HttpStatusCode.OK
                    ? response : """{"error":{"code":"Forbidden","message":"private upstream detail"}}""",
                    Encoding.UTF8, Status == HttpStatusCode.OK ? ContentType : "application/json")
            };
        }
    }
}
