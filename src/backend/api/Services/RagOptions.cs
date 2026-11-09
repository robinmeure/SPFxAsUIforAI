using System.ComponentModel.DataAnnotations;

namespace Chatbot.Api.Services;

public sealed class RagOptions
{
    [Required]
    public string SearchEndpoint { get; set; } = "";

    /// <summary>The API creates this index at startup if it doesn't exist.</summary>
    [Required]
    public string IndexName { get; set; } = "my-documents";

    [Required]
    public string FoundryEndpoint { get; set; } = "";

    [Required]
    public string ModelDeployment { get; set; } = "";

    /// <summary>
    /// Deployment that plans retrieval (scope and search query) before each answer. Defaults to
    /// <see cref="ModelDeployment"/>; a small, fast model shortens the time before the answer starts.
    /// </summary>
    public string? PlannerModelDeployment { get; set; }

    /// <summary>
    /// Reasoning effort for the planner, for example <c>low</c>. Leave empty for non-reasoning planner models,
    /// which reject the setting.
    /// </summary>
    public string? PlannerReasoningEffort { get; set; } = "low";

    [Required]
    public string EmbeddingDeployment { get; set; } = "text-embedding-3-large";

    /// <summary>
    /// Dimensions of the index's <c>contentVector</c> field, used for chunks and queries. Changing it for an existing
    /// index fails at startup; use a new <see cref="IndexName"/> and re-add the documents.
    /// </summary>
    [Range(1, 4096)]
    public int EmbeddingDimensions { get; set; } = 1024;

    [Range(1, 20)]
    public int MaxAgentIterations { get; set; } = 8;

    [Range(0, 100)]
    public int HistoryMessageCount { get; set; } = 20;
}
