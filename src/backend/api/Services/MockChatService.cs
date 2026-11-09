using System.Runtime.CompilerServices;

namespace Chatbot.Api.Services;

public sealed class MockChatService : IChatService
{
    public async IAsyncEnumerable<ChatStreamEvent> StreamAsync(
        ChatTurn turn, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new(Activity: "Understanding your request");
        await Task.Delay(700, cancellationToken);
        yield return new(Activity: "Preparing a response");
        await Task.Delay(700, cancellationToken);
        foreach (var chunk in new[] { "Hello", " Copilot", " from the mock API.", "\n\nThis reply streams in **Markdown**:",
            "\n\n- Activity steps appear while it works", "\n- The answer follows",
            $"\n- This chat has {turn.History.Count(message => message.Role is Storage.ChatMessage.UserRole or Storage.ChatMessage.AssistantRole)} earlier message(s)" })
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(Text: chunk);
            await Task.Delay(180, cancellationToken);
        }
        yield return new(FollowUpQuestions: ["What can you help me with?", "How do I attach a document?"]);
    }
}
