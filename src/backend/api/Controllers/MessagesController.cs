using System.ClientModel;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Chatbot.Api.Models;
using Chatbot.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Controllers;

[ApiController]
[Route("api/messages")]
public sealed class MessagesController(IChatService chatService, ILogger<MessagesController> logger) : ControllerBase
{
    [HttpPost("stream")]
    public async Task Stream(ChatRequest request, CancellationToken cancellationToken)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        try
        {
            await foreach (var update in chatService.StreamAsync(request.Message, cancellationToken))
            {
                await SendAsync(update, cancellationToken);
            }

            await Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
            await Response.Body.FlushAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Chat stream {TraceId} disconnected.", HttpContext.TraceIdentifier);
        }
        catch (Exception exception) when (exception is RequestFailedException
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
    }

    private async Task SendAsync(ChatStreamEvent update, CancellationToken cancellationToken)
    {
        await Response.WriteAsync($"data: {JsonSerializer.Serialize(update)}\n\n", cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
