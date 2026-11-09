using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Chatbot.Api.Services.Storage;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Chatbot.Api.Tests;

public sealed class ChatsAndDocumentsTests
{
    [Fact]
    public async Task UnavailableAttachmentsCanBeRemovedIndividuallyButNotAdded()
    {
        using var factory = new MessagesTests.ChatFactory { MockResponses = true, SeedDocuments = true };
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/messages/stream", new { message = "First", documentIds = new[] { "doc1", "doc2" } });
        var id = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/chats")).EnumerateArray()).GetProperty("id").GetString();
        var registry = factory.Services.GetRequiredService<IDocumentRegistry>();
        await registry.MarkDeletedAsync("local-development-user", "doc1", default);
        await registry.MarkDeletedAsync("local-development-user", "doc2", default);
        await registry.CreateAsync(new UserDocument
        {
            Id = "doc3", UserId = "local-development-user", FileName = "third.txt", SourceType = DocumentSource.Upload
        }, default);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/chats/{id}/documents",
            new { documentIds = new[] { "doc2", "doc3" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/messages/stream",
            new { chatId = id, message = "Follow up", documentIds = new[] { "doc2", "doc3" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/chats/{id}/documents",
            new { documentIds = new[] { "doc1", "doc2" } })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "New", documentIds = new[] { "doc2" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/chats/{id}/documents",
            new { documentIds = new[] { "doc3" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/chats/{id}/documents",
            new { documentIds = Array.Empty<string>() })).StatusCode);
    }

    [Fact]
    public async Task ChatsCanBeListedOpenedAndDeleted()
    {
        using var factory = new MessagesTests.ChatFactory { MockResponses = true };
        using var client = factory.CreateClient();
        var stream = await (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "  Plan   the\nquarterly review  " })).Content.ReadAsStringAsync();
        Assert.EndsWith("data: [DONE]\n\n", stream);

        var chats = await client.GetFromJsonAsync<JsonElement>("/api/chats");
        var chat = Assert.Single(chats.EnumerateArray());
        Assert.Equal("Plan the quarterly review", chat.GetProperty("title").GetString());
        var chatId = chat.GetProperty("id").GetString();

        var details = await client.GetFromJsonAsync<JsonElement>($"/api/chats/{chatId}");
        var messages = details.GetProperty("messages").EnumerateArray().ToList();
        Assert.Equal(["user", "assistant"], messages.Select(message => message.GetProperty("role").GetString()));
        Assert.Contains("0 earlier message(s)", messages[1].GetProperty("content").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/chats/{chatId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/chats/{chatId}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/chats")).EnumerateArray());
    }

    [Fact]
    public async Task UploadedDocumentIsProcessedListedAndDeleted()
    {
        using var factory = new MessagesTests.ChatFactory { MockResponses = true };
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(Encoding.UTF8.GetBytes("Quarterly figures")), "file", "notes.txt" }
        };

        var response = await client.PostAsync("/api/documents", form);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("notes.txt", document.GetProperty("fileName").GetString());
        var documentId = document.GetProperty("id").GetString();

        var status = "";
        for (var attempt = 0; attempt < 50 && status != "ready"; attempt++)
        {
            await Task.Delay(50);
            var listed = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/documents")).EnumerateArray());
            status = listed.GetProperty("status").GetString();
        }

        Assert.Equal("ready", status);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/documents/{documentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/documents/{documentId}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/documents")).EnumerateArray());
    }

    [Theory]
    [InlineData("tool.exe", 10, HttpStatusCode.UnsupportedMediaType)]
    [InlineData("big.pdf", 10 * 1024 * 1024 + 1, HttpStatusCode.RequestEntityTooLarge)]
    public async Task InvalidUploadsAreRejected(string fileName, int size, HttpStatusCode expected)
    {
        using var factory = new MessagesTests.ChatFactory { MockResponses = true };
        using var client = factory.CreateClient();
        using var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[size]), "file", fileName } };
        Assert.Equal(expected, (await client.PostAsync("/api/documents", form)).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/documents")).EnumerateArray());
    }

    [Fact]
    public async Task SharePointDocumentsNeedRealAuthentication()
    {
        using var factory = new MessagesTests.ChatFactory { MockResponses = true };
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/documents/sharepoint", new { driveId = "b!drive", itemId = "item" });
        Assert.False(response.IsSuccessStatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>("/api/documents")).EnumerateArray());
    }

    [Fact]
    public async Task ThreadSelectionPersistsAndRejectsUnavailableDocuments()
    {
        using var factory = new MessagesTests.ChatFactory { MockResponses = true, SeedDocuments = true };
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/messages/stream",
            new { message = "hello", documentIds = new[] { "not-owned" } })).StatusCode);
        await client.PostAsJsonAsync("/api/messages/stream", new { message = "hello", documentIds = new[] { "doc1" } });
        var chat = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/chats")).EnumerateArray());
        var id = chat.GetProperty("id").GetString();
        Assert.Equal("doc1", chat.GetProperty("documentIds")[0].GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/chats/{id}/documents",
            new { documentIds = new[] { "not-owned" } })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/chats/{Guid.NewGuid()}/documents",
            new { documentIds = new[] { "doc1" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/chats/{id}/documents",
            new { documentIds = new[] { "doc2" } })).StatusCode);
        var details = await client.GetFromJsonAsync<JsonElement>($"/api/chats/{id}");
        Assert.Equal("doc2", Assert.Single(details.GetProperty("documentIds").EnumerateArray()).GetString());
        Assert.Equal("upload", Assert.Single(details.GetProperty("documents").EnumerateArray()).GetProperty("sourceType").GetString());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync($"/api/chats/{id}/documents",
            new { documentIds = Array.Empty<string>() })).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<JsonElement>($"/api/chats/{id}")).GetProperty("documentIds").EnumerateArray());
    }

    [Fact]
    public async Task EachDocumentSetBelongsToOneConversation()
    {
        using var factory = new MessagesTests.ChatFactory { MockResponses = true, SeedDocuments = true };
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/messages/stream", new { message = "First", documentIds = new[] { "doc1", "doc2" } });
        var first = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/chats")).EnumerateArray()).GetProperty("id").GetString();

        var duplicate = await client.PostAsJsonAsync("/api/messages/stream", new { message = "Again", documentIds = new[] { "doc2", "doc1" } });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(first, problem.GetProperty("conflictChatId").GetString());
        Assert.Equal("First", problem.GetProperty("conflictTitle").GetString());
        Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/chats")).EnumerateArray());

        await client.PostAsJsonAsync("/api/messages/stream", new { message = "Second", documentIds = new[] { "doc1" } });
        var second = (await client.GetFromJsonAsync<JsonElement>("/api/chats")).EnumerateArray()
            .Single(chat => chat.GetProperty("title").GetString() == "Second").GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/chats/{second}/documents",
            new { documentIds = new[] { "doc1", "doc2" } })).StatusCode);
        Assert.Equal("doc1", Assert.Single((await client.GetFromJsonAsync<JsonElement>($"/api/chats/{second}"))
            .GetProperty("documentIds").EnumerateArray()).GetString());

        await client.PostAsJsonAsync("/api/messages/stream", new { message = "General one" });
        await client.PostAsJsonAsync("/api/messages/stream", new { message = "General two", documentIds = Array.Empty<string>() });
        Assert.Equal(4, (await client.GetFromJsonAsync<JsonElement>("/api/chats")).GetArrayLength());
    }

    [Fact]
    public async Task ChangingTheSetOfAnActiveConversationIsRecordedInTheThreadButNotSentToTheModel()
    {
        using var factory = new MessagesTests.ChatFactory { MockResponses = true, SeedDocuments = true };
        using var client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/messages/stream", new { message = "First", documentIds = new[] { "doc1" } });
        var id = Assert.Single((await client.GetFromJsonAsync<JsonElement>("/api/chats")).EnumerateArray()).GetProperty("id").GetString();

        var response = await (await client.PostAsJsonAsync($"/api/chats/{id}/documents",
            new { documentIds = new[] { "doc2" } })).Content.ReadFromJsonAsync<JsonElement>();
        var change = response.GetProperty("event");
        Assert.Equal("event", change.GetProperty("role").GetString());
        Assert.Equal("Added Other.txt · Removed Housing.docx", change.GetProperty("content").GetString());

        var unchanged = await (await client.PostAsJsonAsync($"/api/chats/{id}/documents",
            new { documentIds = new[] { "doc2" } })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, unchanged.GetProperty("event").ValueKind);

        var followUp = await (await client.PostAsJsonAsync("/api/messages/stream", new { message = "Next", chatId = id })).Content.ReadAsStringAsync();
        Assert.Contains("2 earlier message(s)", followUp);
        var roles = (await client.GetFromJsonAsync<JsonElement>($"/api/chats/{id}")).GetProperty("messages").EnumerateArray()
            .Select(message => message.GetProperty("role").GetString());
        Assert.Equal(["user", "assistant", "event", "user", "assistant"], roles);
    }
}
