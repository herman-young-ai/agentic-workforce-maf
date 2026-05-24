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
/// </summary>
public static class AgentJsonShapes
{
    /// <summary>JsonSerializerOptions to use for every read and every write of these shapes.</summary>
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
