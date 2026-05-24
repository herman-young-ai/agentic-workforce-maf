using System.Text.Json;
using System.Text.Json.Nodes;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Agents.Verification;

/// <summary>
/// Tier 1 — fast, deterministic JSON-shape validation against the agent's
/// declared <c>interface.output_schema</c>. Phase 7e ships shape-only checks
/// (parses as JSON, every required field is present, primitive types match);
/// full JSON-Schema draft-2020 support lands in Phase 11 polish.
///
/// <para><b>Why structural-only is enough for Phase 7</b></para>
/// Most regressions in agent output are "the LLM ignored the schema entirely"
/// (returned prose, returned an array where an object was expected, missed
/// required fields). The cheap deterministic check catches all of these and
/// keeps Tier 3 budget reserved for cases where structure is fine but content
/// is questionable.
/// </summary>
internal sealed class SchemaVerifier
{
    public VerificationResult Verify(string output, AgentCatalog agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (string.IsNullOrWhiteSpace(agent.Interface)) return VerificationResult.Pass;

        AgentInterface? iface;
        try
        {
            iface = JsonSerializer.Deserialize<AgentInterface>(agent.Interface, AgentJsonShapes.Options);
        }
        catch (JsonException ex)
        {
            // The interface itself is malformed — that is a catalog bug, not an
            // agent-output bug. Fail Tier 1 loudly so an operator notices.
            return VerificationResult.Fail(FailureTier.Tier1Structural,
                $"AgentCatalog.Interface for {agent.AgentName} is not valid JSON: {ex.Message}");
        }

        if (iface?.OutputSchema is null) return VerificationResult.Pass;

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(output);
        }
        catch (JsonException ex)
        {
            return VerificationResult.Fail(FailureTier.Tier1Structural,
                $"Output is not valid JSON: {ex.Message}",
                feedback: "Return ONLY a JSON document that conforms to the agent's output_schema. No leading prose, no fenced code blocks.");
        }

        if (node is null)
        {
            return VerificationResult.Fail(FailureTier.Tier1Structural,
                "Output parsed to JSON null; expected the object/array declared by output_schema.");
        }

        return ValidateRequiredProperties(node, iface.OutputSchema.Value);
    }

    private static VerificationResult ValidateRequiredProperties(JsonNode output, JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object) return VerificationResult.Pass;

        // Top-level type check.
        if (schema.TryGetProperty("type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String)
        {
            var expected = typeProp.GetString();
            var actual = output switch
            {
                JsonObject => "object",
                JsonArray  => "array",
                JsonValue v when v.TryGetValue<string>(out _)  => "string",
                JsonValue v when v.TryGetValue<bool>(out _)    => "boolean",
                JsonValue v when v.TryGetValue<int>(out _)
                            || v.TryGetValue<long>(out _)
                            || v.TryGetValue<double>(out _)
                            || v.TryGetValue<decimal>(out _)   => "number",
                _ => "unknown"
            };
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                return VerificationResult.Fail(FailureTier.Tier1Structural,
                    $"Output type was '{actual}'; output_schema declares type '{expected}'.",
                    feedback: $"Return a JSON {expected} matching the schema.");
            }
        }

        // Required-property check (object only).
        if (output is JsonObject obj
            && schema.TryGetProperty("required", out var required)
            && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var prop in required.EnumerateArray())
            {
                var name = prop.GetString();
                if (name is null) continue;
                if (!obj.ContainsKey(name))
                {
                    return VerificationResult.Fail(FailureTier.Tier1Structural,
                        $"Output is missing required property '{name}'.",
                        feedback: $"Add the required '{name}' field as declared by output_schema.");
                }
            }
        }

        return VerificationResult.Pass;
    }
}
