using System.ComponentModel.DataAnnotations;

namespace Chatbot.Api.Models;

public sealed record ChatRequest(
    [Required, StringLength(8000, MinimumLength = 1)] string Message);
