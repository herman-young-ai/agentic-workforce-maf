using System.Text.Json;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// One-direction mapping <see cref="AgentSeedDefinition"/> (YAML shape) →
/// <see cref="AgentCatalog"/> (DB entity). Every jsonb column is serialised
/// with <see cref="AgentJsonShapes.Options"/> so the runtime reads the same
/// canonical shape it was written with.
///
/// <para><b>Why a dedicated mapper</b></para>
/// The YAML shape uses operator-friendly snake_case keys and tolerates nullable
/// nested objects. The runtime jsonb shape uses camelCase, is non-tolerant, and
/// is the contract consumed by AgentFactory + ContextAssembler + verification.
/// Centralising the translation here means the wire shape has exactly one author.
/// </summary>
internal static class AgentSeedMapper
{
    public static AgentCatalog ToEntity(AgentSeedDefinition def)
    {
        ArgumentNullException.ThrowIfNull(def);
        var entity = new AgentCatalog { AgentName = def.AgentName };
        ApplyAll(entity, def);
        return entity;
    }

    public static void Update(AgentCatalog existing, AgentSeedDefinition def)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(def);
        ApplyAll(existing, def);
    }

    private static void ApplyAll(AgentCatalog entity, AgentSeedDefinition def)
    {
        entity.AgentType        = def.AgentType;
        entity.AgentVersion     = def.AgentVersion;
        entity.Description      = def.Description;
        entity.SystemPrompt     = def.SystemPrompt;
        entity.Visibility       = ParseVisibility(def.Visibility);
        entity.ChatEnabled      = def.ChatEnabled;
        entity.ProducesArtifact = def.ProducesArtifact;
        entity.ArtifactType     = def.ArtifactType;
        entity.MaxInputLength   = def.Scope?.MaxInputLength;
        entity.MaxBudgetUsd     = def.Scope?.MaxBudgetUsd;
        // Enabled stays at default true on insert; existing flag preserved on update unless
        // a future YAML key explicitly requests disablement (Principle 14 — Secure by Default).

        entity.ModelConfig    = SerialiseModelConfig(def.ModelConfig);
        entity.Tools          = SerialiseTools(def.Tools);
        entity.Scope          = SerialiseScope(def.Scope);
        entity.Constraints    = SerialiseConstraints(def.Constraints);
        entity.Interface      = SerialiseInterface(def.Interface);
        entity.ThinkingBudget = SerialiseThinking(def.ThinkingBudget);
    }

    private static AgentVisibility ParseVisibility(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return AgentVisibility.Internal;
        if (Enum.TryParse<AgentVisibility>(raw, ignoreCase: true, out var parsed)) return parsed;
        throw new FormatException(
            $"Invalid agent visibility '{raw}'. Expected one of: {string.Join(", ", Enum.GetNames<AgentVisibility>())}.");
    }

    private static string? SerialiseModelConfig(AgentSeedDefinition.ModelConfigSection? section)
    {
        if (section is null) return null;
        var shape = new AgentModelConfig(
            Provider:        section.Provider,
            Model:           section.Model,
            Temperature:     section.Temperature,
            MaxOutputTokens: section.MaxOutputTokens);
        return JsonSerializer.Serialize(shape, AgentJsonShapes.Options);
    }

    private static string? SerialiseTools(List<AgentSeedDefinition.ToolBindingSection>? tools)
    {
        if (tools is null || tools.Count == 0) return null;
        var shapes = tools
            .Select(t => new AgentToolBindingShape(t.Name, t.RequiresApproval, t.McpServer))
            .ToArray();
        return JsonSerializer.Serialize(shapes, AgentJsonShapes.Options);
    }

    private static string? SerialiseScope(AgentSeedDefinition.ScopeSection? scope)
    {
        if (scope is null) return null;
        AgentFileScope? fs = scope.FileScope is null
            ? null
            : new AgentFileScope(
                AllowedPaths: scope.FileScope.AllowedPaths?.ToArray() ?? [],
                DeniedPaths:  scope.FileScope.DeniedPaths?.ToArray()  ?? []);
        var shape = new AgentScope(fs, scope.MaxInputLength, scope.MaxBudgetUsd);
        return JsonSerializer.Serialize(shape, AgentJsonShapes.Options);
    }

    private static string? SerialiseConstraints(AgentSeedDefinition.ConstraintsSection? c)
    {
        if (c is null) return null;
        var shape = new AgentConstraints(c.MaxToolCalls, c.TimeoutSeconds, c.RequireStructuredOutput);
        return JsonSerializer.Serialize(shape, AgentJsonShapes.Options);
    }

    private static string? SerialiseInterface(AgentSeedDefinition.InterfaceSection? iface)
    {
        if (iface is null) return null;
        // YamlDotNet deserialises the schema sub-trees as Dictionary<object, object>.
        // Round-tripping via JsonSerializer.SerializeToElement gives us a stable JsonElement
        // that we then wrap in AgentInterface — same shape the runtime parses.
        var input  = ToJsonElement(iface.InputSchema);
        var output = ToJsonElement(iface.OutputSchema);
        var shape  = new AgentInterface(input, output);
        return JsonSerializer.Serialize(shape, AgentJsonShapes.Options);
    }

    private static string? SerialiseThinking(AgentSeedDefinition.ThinkingBudgetSection? t)
    {
        if (t is null) return null;
        var shape = new AgentThinkingBudget(t.Enabled, t.MaxTokens);
        return JsonSerializer.Serialize(shape, AgentJsonShapes.Options);
    }

    private static JsonElement? ToJsonElement(object? yamlNode)
    {
        if (yamlNode is null) return null;
        // YamlDotNet hands back Dictionary<object, object> / List<object> / string. System.Text.Json
        // doesn't natively serialise object-keyed dictionaries, so we normalise into a dictionary
        // with string keys first.
        var normalised = NormaliseKeys(yamlNode);
        var json = JsonSerializer.Serialize(normalised, AgentJsonShapes.Options);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static object? NormaliseKeys(object? node) => node switch
    {
        IDictionary<object, object?> dict => dict.ToDictionary(
            kv => kv.Key?.ToString() ?? string.Empty,
            kv => NormaliseKeys(kv.Value)),
        IList<object?> list => list.Select(NormaliseKeys).ToList(),
        _ => node
    };
}
