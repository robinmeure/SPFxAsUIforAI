using Chatbot.Api.Services.Agent;
using Microsoft.AspNetCore.Mvc;

namespace Chatbot.Api.Controllers;

public sealed record SkillSummary(string Name, string Description);

/// <summary>The skills a user can pick for a message; the same catalog the model sees.</summary>
[ApiController]
[Route("api/skills")]
public sealed class SkillsController(SkillCatalog catalog) : ControllerBase
{
    [HttpGet]
    public IReadOnlyList<SkillSummary> List() =>
        catalog.Skills.OrderBy(skill => skill.Name, StringComparer.Ordinal)
            .Select(skill => new SkillSummary(skill.Name, skill.Description)).ToList();
}
