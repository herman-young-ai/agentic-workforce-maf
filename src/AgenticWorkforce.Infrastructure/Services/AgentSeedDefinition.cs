using System.Text.Json;
using YamlDotNet.Serialization;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// In-memory shape of one <c>Catalog/Seeds/*.yaml</c> file. YamlDotNet deserialises
/// into this record; <see cref="AgentSeedMapper"/> converts it to an
/// <see cref="AgenticWorkforce.Domain.Entities.AgentCatalog"/> row using the
/// canonical jsonb shapes defined in <c>AgentJsonShapes</c>.
///
/// <para><b>Why a separate shape</b></para>
/// Domain owns the canonical jsonb records (read by every runtime consumer). The
/// YAML shape mirrors operator-facing snake_case keys and tolerates the structural
/// quirks of YAML (nullable nested objects, free-form schema documents). Keeping
/// the two shapes distinct prevents YAML quirks from leaking into the runtime
/// types.
/// </para>
/// </summary>
internal sealed class AgentSeedDefinition
{
    [YamlMember(Alias = "agent_name")]      public string AgentName { get; set; } = null!;
    [YamlMember(Alias = "agent_type")]      public string? AgentType { get; set; }
    [YamlMember(Alias = "agent_version")]   public string AgentVersion { get; set; } = null!;
    [YamlMember(Alias = "description")]     public string? Description { get; set; }
    [YamlMember(Alias = "visibility")]      public string? Visibility { get; set; }
    [YamlMember(Alias = "chat_enabled")]    public bool ChatEnabled { get; set; }
    [YamlMember(Alias = "produces_artifact")] public bool ProducesArtifact { get; set; }
    [YamlMember(Alias = "artifact_type")]   public string? ArtifactType { get; set; }
    [YamlMember(Alias = "model_config")]    public ModelConfigSection? ModelConfig { get; set; }
    [YamlMember(Alias = "tools")]           public List<ToolBindingSection>? Tools { get; set; }
    [YamlMember(Alias = "scope")]           public ScopeSection? Scope { get; set; }
    [YamlMember(Alias = "constraints")]     public ConstraintsSection? Constraints { get; set; }
    [YamlMember(Alias = "interface")]       public InterfaceSection? Interface { get; set; }
    [YamlMember(Alias = "thinking_budget")] public ThinkingBudgetSection? ThinkingBudget { get; set; }
    [YamlMember(Alias = "system_prompt")]   public string? SystemPrompt { get; set; }

    internal sealed class ModelConfigSection
    {
        [YamlMember(Alias = "provider")]          public string Provider { get; set; } = null!;
        [YamlMember(Alias = "model")]             public string Model { get; set; } = null!;
        [YamlMember(Alias = "temperature")]       public double? Temperature { get; set; }
        [YamlMember(Alias = "max_output_tokens")] public int? MaxOutputTokens { get; set; }
    }

    internal sealed class ToolBindingSection
    {
        [YamlMember(Alias = "name")]              public string Name { get; set; } = null!;
        [YamlMember(Alias = "requires_approval")] public bool RequiresApproval { get; set; }
        [YamlMember(Alias = "mcp_server")]        public string? McpServer { get; set; }
    }

    internal sealed class ScopeSection
    {
        [YamlMember(Alias = "file_scope")]        public FileScopeSection? FileScope { get; set; }
        [YamlMember(Alias = "max_input_length")]  public int? MaxInputLength { get; set; }
        [YamlMember(Alias = "max_budget_usd")]    public decimal? MaxBudgetUsd { get; set; }
    }

    internal sealed class FileScopeSection
    {
        [YamlMember(Alias = "allowed_paths")] public List<string>? AllowedPaths { get; set; }
        [YamlMember(Alias = "denied_paths")]  public List<string>? DeniedPaths { get; set; }
    }

    internal sealed class ConstraintsSection
    {
        [YamlMember(Alias = "max_tool_calls")]            public int? MaxToolCalls { get; set; }
        [YamlMember(Alias = "timeout_seconds")]           public int? TimeoutSeconds { get; set; }
        [YamlMember(Alias = "require_structured_output")] public bool? RequireStructuredOutput { get; set; }
    }

    internal sealed class InterfaceSection
    {
        // Free-form JSON schema documents — kept as object trees so the YAML→JSON
        // serialiser produces a faithful structure. Validation lives in SchemaVerifier.
        [YamlMember(Alias = "input_schema")]  public object? InputSchema { get; set; }
        [YamlMember(Alias = "output_schema")] public object? OutputSchema { get; set; }
    }

    internal sealed class ThinkingBudgetSection
    {
        [YamlMember(Alias = "enabled")]    public bool Enabled { get; set; }
        [YamlMember(Alias = "max_tokens")] public int? MaxTokens { get; set; }
    }
}
