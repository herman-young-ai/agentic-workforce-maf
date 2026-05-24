using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgenticWorkforce.Domain.Agents;

/// <summary>
/// Canonical jsonb shapes stored on <see cref="Entities.AgentCatalog"/>'s
/// jsonb columns. Defined in Domain so every consumer (seeder write path,
/// agent factory read path, verification pipeline, future tool dispatcher)
/// parses the same types — no per-consumer parsing drift.
///
/// <para><b>Canonical encoding</b></para>
/// Producers and consumers both use camelCase via
/// <c>JsonNamingPolicy.CamelCase</c>. The records are immutable so a write +
/// read round trip is bit-equal modulo whitespace.
///
/// <para><b>About <see cref="Options"/></b></para>
/// The <see cref="Options"/> instance is the canonical camelCase-web profile
/// for every Domain / Agents-side serialise + deserialise — both the jsonb
/// columns above AND the JSON tool-output strings returned to the LLM. The
/// Agents layer cannot reference <c>Infrastructure.Events.WireJsonOptions</c>,
/// so this is its default. Treat <see cref="Options"/> as "the Domain layer's
/// web JSON options", not as "options for these records only".
/// </summary>
public static class AgentJsonShapes
{
    /// <summary>
    /// Canonical camelCase JSON options for every Domain / Agents-side
    /// serialise + deserialise. See type docs for the broader-than-it-sounds scope.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };
}

public sealed record AgentModelConfig(
    string Provider,
    string Model,
    double? Temperature,
    int? MaxOutputTokens);

public sealed record AgentToolBindingShape(
    string Name,
    bool RequiresApproval = false,
    string? McpServer = null);

public sealed record AgentScope(
    AgentFileScope? FileScope,
    int? MaxInputLength,
    decimal? MaxBudgetUsd);

public sealed record AgentFileScope(
    string[] AllowedPaths,
    string[] DeniedPaths);

public sealed record AgentConstraints(
    int? MaxToolCalls,
    int? TimeoutSeconds,
    bool? RequireStructuredOutput);

public sealed record AgentInterface(
    JsonElement? InputSchema,
    JsonElement? OutputSchema);

public sealed record AgentThinkingBudget(
    bool Enabled,
    int? MaxTokens);
