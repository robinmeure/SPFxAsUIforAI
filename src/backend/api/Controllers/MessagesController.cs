using System.ClientModel;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Chatbot.Api.Models;
using Chatbot.Api.Services;
using Chatbot.Api.Services.Agent;
using Chatbot.Api.Services.Storage;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

namespace Chatbot.Api.Controllers;

[ApiController]
[Route("api/messages")]
public sealed class MessagesController(
    IChatService chatService,
    IChatStore chatStore,
    ChatDocumentSets documentSets,
    SkillCatalog skills,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<MessagesController> logger) : ControllerBase
{
    private const int TitleLength = 80;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [HttpPost("stream")]
    public async Task<IActionResult> Stream(ChatRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId(configuration);
        if (request.Skill is not null && skills.Find(request.Skill) is null)
        {
            return BadRequest(new ProblemDetails { Detail = $"Unknown skill '{request.Skill}'." });
        }

        IReadOnlyList<ChatMessage> history = [];
        ChatSummary? chat = null;
        if (request.ChatId is not null)
        {
            chat = await chatStore.GetAsync(userId, request.ChatId, cancellationToken);
            if (chat is null)
            {
                return NotFound();
            }

        }

        var documentIds = chat?.DocumentIds ?? [];
        if (request.DocumentIds is not null)
        {
            switch (await documentSets.CheckAsync(userId, chat?.Id, request.DocumentIds, cancellationToken))
            {
                case DocumentSetResult.Updated updated:
                    documentIds = updated.DocumentIds;
                    break;
                case DocumentSetResult.Duplicate duplicate:
                    return ChatsController.DuplicateSet(this, duplicate.Conflict);
                case DocumentSetResult.Invalid invalid:
                    return BadRequest(new ProblemDetails { Detail = invalid.Detail });
            }
        }

        if (chat is not null)
        {
            history = await chatStore.GetMessagesAsync(userId, chat.Id, cancellationToken);
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            chat ??= await chatStore.CreateAsync(userId, CreateTitle(request.Message), cancellationToken);
            if (request.DocumentIds is not null
                && await documentSets.ApplyAsync(userId, chat, documentIds, cancellationToken) is not DocumentSetResult.Updated)
            {
                throw new InvalidOperationException("The document set changed while the message was being sent.");
            }
            await SendAsync(new ChatStreamEvent(ChatId: chat.Id), cancellationToken);
            await chatStore.AddMessageAsync(userId, chat.Id, new ChatMessage(Guid.NewGuid().ToString(),
                ChatMessage.UserRole, request.Message, time.GetUtcNow()) { Skill = request.Skill }, cancellationToken);

            var answer = new StringBuilder();
            IReadOnlyList<PassageCitation> citations = [];
            RetrievalDetails? retrieval = null;
            IReadOnlyList<string> followUpQuestions = [];
            await foreach (var update in chatService.StreamAsync(new ChatTurn(userId, history, request.Message)
                { DocumentIds = documentIds, Skill = request.Skill }, cancellationToken))
            {
                answer.Append(update.Text);
                citations = update.Citations ?? citations;
                retrieval = update.Retrieval ?? retrieval;
                followUpQuestions = update.FollowUpQuestions ?? followUpQuestions;
                await SendAsync(update, cancellationToken);
            }

            await chatStore.AddMessageAsync(userId, chat.Id, new ChatMessage(Guid.NewGuid().ToString(),
                ChatMessage.AssistantRole, answer.ToString(), time.GetUtcNow())
                { Citations = citations, Retrieval = retrieval, FollowUpQuestions = followUpQuestions }, cancellationToken);
            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Chat stream {TraceId} disconnected.", HttpContext.TraceIdentifier);
        }
        catch (Exception exception) when (exception is RequestFailedException or CosmosException
            or ClientResultException or AuthenticationFailedException or CredentialUnavailableException
            or InvalidOperationException or HttpRequestException or OperationCanceledException or JsonException)
        {
            logger.LogError(exception, "Chat stream {TraceId} failed.", HttpContext.TraceIdentifier);
            if (!cancellationToken.IsCancellationRequested)
            {
                await SendAsync(new ChatStreamEvent(Error:
                    $"The response could not be completed. Please try again. Reference: {HttpContext.TraceIdentifier}"),
                    cancellationToken);
            }
        }

        return new EmptyResult();
    }

    private static string CreateTitle(string message)
    {
        var title = string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return title.Length <= TitleLength ? title : title[..(TitleLength - 1)] + "…";
    }

    private async Task SendAsync(ChatStreamEvent update, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(update, JsonOptions)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
