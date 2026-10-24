using System.ComponentModel.DataAnnotations;

namespace Chatbot.Api.Services;

public sealed class RagOptions
{
    [Required]
    public string SearchEndpoint { get; set; } = "";

    [Required]
    public string IndexName { get; set; } = "";

    [Required]
    public string FoundryEndpoint { get; set; } = "";

    [Required]
    public string ModelDeployment { get; set; } = "";
}
