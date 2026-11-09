using Chatbot.Api.Models;
using Chatbot.Api.Services.Storage;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Controllers;

[ApiController]
[Route("api/chats")]
public sealed class ChatsController(IChatStore chatStore, IDocumentRegistry documents, ChatDocumentSets documentSets,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<ChatSummary>> List(CancellationToken cancellationToken) =>
        await chatStore.ListAsync(User.GetUserId(configuration), cancellationToken);

    [HttpGet("{chatId:guid}")]
    public async Task<ActionResult<ChatDetails>> Get(Guid chatId, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId(configuration);
        var chat = await chatStore.GetAsync(userId, chatId.ToString(), cancellationToken);
        if (chat is null)
        {
            return NotFound();
        }

        var messages = await chatStore.GetMessagesAsync(userId, chat.Id, cancellationToken);
        var attached = (await documents.ListAsync(userId, cancellationToken)).Where(document => chat.DocumentIds.Contains(document.Id)).ToList();
        return new ChatDetails(
            chat.Id, 
            chat.Title, 
            chat.CreatedAt, 
            chat.UpdatedAt,
            messages.Select(message => new ChatMessageDto(message.Id, message.Role, message.Content, message.CreatedAt,
                message.Citations, message.Retrieval, message.Skill)
                { FollowUpQuestions = message.FollowUpQuestions }).ToList(), chat.DocumentIds, attached);
    }

    [HttpPost("{chatId:guid}/documents")]
    public async Task<IActionResult> SetDocuments(Guid chatId, ChatDocumentsRequest request, CancellationToken cancellationToken)
    {
        var userId = User.GetUserId(configuration);
        var chat = await chatStore.GetAsync(userId, chatId.ToString(), cancellationToken);
        if (chat is null)
        {
            return NotFound();
        }

        return await documentSets.ApplyAsync(userId, chat, request.DocumentIds, cancellationToken) switch
        {
            DocumentSetResult.Updated updated => Ok(new ChatDocumentsResponse(updated.DocumentIds, updated.Event is { } change
                ? new ChatMessageDto(change.Id, change.Role, change.Content, change.CreatedAt, [], null) : null)),
            DocumentSetResult.Duplicate duplicate => DuplicateSet(this, duplicate.Conflict),
            DocumentSetResult.Invalid invalid => BadRequest(new ProblemDetails { Detail = invalid.Detail }),
            _ => throw new InvalidOperationException("Unexpected document set result.")
        };
    }

    internal static ObjectResult DuplicateSet(ControllerBase controller, DocumentSetConflict conflict)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Duplicate document set",
            Detail = $"You already have a conversation about these documents: \"{conflict.Title}\".",
            Extensions = { ["conflictChatId"] = conflict.ChatId, ["conflictTitle"] = conflict.Title }
        };
        return controller.Conflict(problem);
    }

    [HttpDelete("{chatId:guid}")]
    public async Task<IActionResult> Delete(Guid chatId, CancellationToken cancellationToken) =>
        await chatStore.DeleteAsync(User.GetUserId(configuration), chatId.ToString(), cancellationToken) ? NoContent() : NotFound();
}
