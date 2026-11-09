using System.Diagnostics;

namespace Chatbot.Api.Services;

/// <summary>Spans for the chat pipeline phases, so traces show where a turn spends its time.</summary>
public static class Telemetry
{
    public const string SourceName = "Chatbot.Api";
    public static readonly ActivitySource Source = new(SourceName);
}
