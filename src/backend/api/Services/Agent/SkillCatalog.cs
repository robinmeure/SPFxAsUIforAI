using System.Text;
using System.Text.RegularExpressions;

namespace Chatbot.Api.Services.Agent;

public sealed record Skill(string Name, string Description, string Instructions, IReadOnlyDictionary<string, string> ResourcePaths);

/// <summary>
/// Loads Agent Skills from <c>skills/&lt;name&gt;/SKILL.md</c> (YAML front matter with <c>name</c> and <c>description</c>,
/// followed by Markdown instructions). Only the name and description go into the system prompt; the model loads the
/// instructions and resource files on demand. Skills are instructions only: scripts are never executed.
/// </summary>
public sealed partial class SkillCatalog
{
    private const int MaxResourceBytes = 256 * 1024;
    private static readonly HashSet<string> ResourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".json", ".csv", ".yaml", ".yml", ".xml", ".html"
    };

    private readonly Dictionary<string, Skill> _skills;

    public SkillCatalog(string skillsDirectory, ILogger<SkillCatalog> logger)
    {
        _skills = new Dictionary<string, Skill>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(skillsDirectory))
        {
            logger.LogInformation("No skills directory found at {Directory}.", skillsDirectory);
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(skillsDirectory).Order(StringComparer.Ordinal))
        {
            var skillFile = Path.Combine(directory, "SKILL.md");
            if (!File.Exists(skillFile))
            {
                continue;
            }

            try
            {
                var skill = Parse(Path.GetFileName(directory), File.ReadAllText(skillFile), EnumerateResources(directory));
                _skills[skill.Name] = skill;
            }
            catch (FormatException exception)
            {
                logger.LogError("Skipping skill in {Directory}: {Reason}", directory, exception.Message);
            }
        }

        logger.LogInformation("Loaded {Count} skill(s): {Names}", _skills.Count, string.Join(", ", _skills.Keys));
    }

    public IReadOnlyCollection<Skill> Skills => _skills.Values;

    public Skill? Find(string name) => _skills.GetValueOrDefault(name);

    public string? ReadResource(Skill skill, string relativePath)
    {
        var key = relativePath.Replace('\\', '/').TrimStart('/');
        return skill.ResourcePaths.TryGetValue(key, out var fullPath) ? File.ReadAllText(fullPath) : null;
    }

    public string DescribeForPrompt()
    {
        if (_skills.Count == 0)
        {
            return "";
        }

        var builder = new StringBuilder();
        foreach (var skill in _skills.Values)
        {
            builder.Append("- ").Append(skill.Name).Append(": ").AppendLine(skill.Description);
        }

        return builder.ToString();
    }

    public static Skill Parse(string folderName, string content, IReadOnlyDictionary<string, string> resources)
    {
        content = content.Replace("\r\n", "\n");
        var match = FrontMatterRegex().Match(content);
        if (!match.Success)
        {
            throw new FormatException("SKILL.md must start with YAML front matter between '---' lines.");
        }

        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in match.Groups[1].Value.Split('\n'))
        {
            var separator = line.IndexOf(':');
            if (separator > 0 && !char.IsWhiteSpace(line[0]))
            {
                fields[line[..separator].Trim()] = line[(separator + 1)..].Trim().Trim('"', '\'');
            }
        }

        var name = fields.GetValueOrDefault("name") ?? "";
        var description = fields.GetValueOrDefault("description") ?? "";
        if (name.Length > 64 || !NameRegex().IsMatch(name) || !string.Equals(name, folderName, StringComparison.Ordinal))
        {
            throw new FormatException("'name' must be lowercase letters, digits and hyphens (max 64) and match the folder name.");
        }

        if (description.Length is 0 or > 1024)
        {
            throw new FormatException("'description' is required and must be at most 1024 characters.");
        }

        return new Skill(name, description, content[(match.Index + match.Length)..].Trim(), resources);
    }

    private static Dictionary<string, string> EnumerateResources(string directory)
    {
        var resources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var subfolder in new[] { "references", "assets" })
        {
            var path = Path.Combine(directory, subfolder);
            if (!Directory.Exists(path))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                if (ResourceExtensions.Contains(Path.GetExtension(file)) && new FileInfo(file).Length <= MaxResourceBytes)
                {
                    resources[Path.GetRelativePath(directory, file).Replace('\\', '/')] = file;
                }
            }
        }

        return resources;
    }

    [GeneratedRegex(@"\A---\n(.*?)\n---\n?", RegexOptions.Singleline)]
    private static partial Regex FrontMatterRegex();

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex NameRegex();
}
