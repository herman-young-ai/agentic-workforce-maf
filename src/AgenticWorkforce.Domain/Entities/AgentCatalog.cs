namespace AgenticWorkforce.Domain.Entities;

/// <summary>
/// Platform-wide agent catalog entry. Defines an agent's identity, model, prompt, and tools.
/// Versioned via SemanticVersion — immutable once published.
/// </summary>
public class AgentCatalog : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SystemPrompt { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string SemanticVersion { get; set; } = "1.0.0";

    public AgentExecutionMode ExecutionMode { get; set; } = AgentExecutionMode.Sandbox;

    public int MaxTokensPerTurn { get; set; } = 16_000;
    public int MaxTurnsPerExecution { get; set; } = 25;
    public double Temperature { get; set; } = 0.3;
    public double? TopP { get; set; }

    /// <summary>Tool manifest (jsonb). List of tool names this agent can invoke.</summary>
    public string? Tools { get; set; }

    /// <summary>MCP server URIs (jsonb).</summary>
    public string? McpServers { get; set; }

    public bool IsActive { get; set; } = true;
    public bool IsBuiltIn { get; set; }

    // Navigation properties
    public ICollection<ProjectAgent> ProjectAgents { get; set; } = [];
    public ICollection<TemplateAgent> TemplateAgents { get; set; } = [];
}

/// <summary>
/// Reusable team composition template. Projects can inherit from a template
/// to get a pre-configured set of agents.
/// </summary>
public class AgentTemplate : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string SemanticVersion { get; set; } = "1.0.0";

    /// <summary>Optional parent template for inheritance.</summary>
    public Guid? ParentTemplateId { get; set; }
    public AgentTemplate? ParentTemplate { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation properties
    public ICollection<TemplateAgent> Agents { get; set; } = [];
}

/// <summary>Join table: which agents belong to which template, with role overrides.</summary>
public class TemplateAgent : EntityBase
{
    public Guid TemplateId { get; set; }
    public AgentTemplate Template { get; set; } = null!;

    public Guid AgentId { get; set; }
    public AgentCatalog Agent { get; set; } = null!;

    /// <summary>Role override within this template (e.g., "lead", "reviewer").</summary>
    public string? RoleInTeam { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// Per-project agent assignment. Allows project-level prompt overrides
/// and tool restrictions beyond the catalog defaults.
/// </summary>
public class ProjectAgent : ProjectScopedEntity
{
    public Guid AgentId { get; set; }
    public AgentCatalog Agent { get; set; } = null!;

    /// <summary>Project-specific system prompt override (appended to catalog prompt).</summary>
    public string? PromptOverride { get; set; }

    /// <summary>Project-specific tool allowlist (jsonb). Null = use catalog defaults.</summary>
    public string? ToolOverrides { get; set; }

    /// <summary>Role within this project's team.</summary>
    public string? RoleInTeam { get; set; }

    public bool IsActive { get; set; } = true;
}
