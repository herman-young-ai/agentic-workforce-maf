using System.Reflection;
using System.Text;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Exceptions;

namespace AgenticWorkforce.Agents.Prompts;

internal sealed class PromptAssembler : IPromptAssembler
{
    private const string OrgResourcePrefix = "AgenticWorkforce.Agents.Prompts.Organization.";
    private const string CategoryResourcePrefix = "AgenticWorkforce.Agents.Prompts.Categories.";

    private static readonly string[] OrgLayerNames =
    [
        "principles.md",
        "coding-standards.md",
        "security-posture.md"
    ];

    private readonly Lazy<string> _organizationLayer;

    public PromptAssembler()
    {
        _organizationLayer = new Lazy<string>(LoadOrganizationLayer);
    }

    public Task<string> AssembleAsync(
        AgentCatalog agent,
        Project project,
        ProjectAgent? projectAgent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(project);

        var sb = new StringBuilder(capacity: 4096);

        // Layer 1: Organization (generated from docs/ at build time)
        sb.AppendLine("# Organization\n");
        sb.AppendLine(_organizationLayer.Value);

        // Layer 2: Category (embedded resource per AgentCatalog.AgentType)
        var categoryFile = (agent.AgentType ?? "system").ToLowerInvariant() + ".md";
        sb.AppendLine("\n# Category\n");
        sb.AppendLine(LoadEmbeddedResource(CategoryResourcePrefix + categoryFile)
            ?? throw new InvalidStateException(
                $"AgentCatalog '{agent.AgentName}' has AgentType '{agent.AgentType}' but no embedded prompt at {categoryFile}."));

        // Layer 3: Agent system prompt
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
        {
            sb.AppendLine("\n# Agent\n");
            sb.AppendLine(agent.SystemPrompt);
        }

        // Layer 4: Project brief
        if (!string.IsNullOrWhiteSpace(project.Brief))
        {
            sb.AppendLine("\n# Project Brief\n");
            sb.AppendLine(project.Brief);
        }

        // Layer 5: User-customised prompt for this Project + Agent pair
        if (projectAgent is not null && !string.IsNullOrWhiteSpace(projectAgent.UserPrompt))
        {
            sb.AppendLine("\n# Project-Specific Instructions\n");
            sb.AppendLine(projectAgent.UserPrompt);
        }

        return Task.FromResult(sb.ToString());
    }

    private static string LoadOrganizationLayer()
    {
        var sb = new StringBuilder();
        foreach (var name in OrgLayerNames)
        {
            var content = LoadEmbeddedResource(OrgResourcePrefix + name)
                ?? throw new InvalidStateException(
                    $"Organization prompt '{name}' is missing. Build target GenerateOrganizationPrompts should have generated it from docs/.");
            sb.AppendLine(content);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string? LoadEmbeddedResource(string resourceName)
    {
        var asm = typeof(PromptAssembler).GetTypeInfo().Assembly;
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
