using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgenticWorkforce.Infrastructure.Services;

/// <summary>
/// Loads every <c>*.Catalog.Seeds.*.yaml</c> embedded resource from the supplied
/// assembly and deserialises into <see cref="AgentSeedDefinition"/>.
///
/// <para><b>Why the assembly is constructor-injected</b></para>
/// The seed YAMLs are owned by <c>AgenticWorkforce.Agents</c>, but the layer graph
/// forbids <c>Infrastructure → Agents</c>. The Worker (which references both)
/// hands the Agents assembly handle into this source via DI registration.
///
/// <para><b>Strict deserialisation (Principle 8)</b></para>
/// Deserialisation rejects unknown fields' impact on missing required values
/// (empty agent_name / agent_version) by throwing in <see cref="Load"/>. The
/// default scalar resolver is used — no implicit type tags, no
/// Python-style object instantiation from YAML.
/// </summary>
internal sealed class EmbeddedYamlAgentSeedSource(Assembly seedAssembly) : IAgentSeedSource
{
    private const string ResourceInfix = ".Catalog.Seeds.";

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public IReadOnlyList<AgentSeedDefinition> Load()
    {
        var resources = seedAssembly.GetManifestResourceNames()
            .Where(n => n.Contains(ResourceInfix, StringComparison.Ordinal)
                     && n.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var definitions = new List<AgentSeedDefinition>(resources.Length);
        foreach (var resource in resources)
        {
            using var stream = seedAssembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resource}' could not be opened — assembly packaging is broken.");
            using var reader = new StreamReader(stream);
            var def = Yaml.Deserialize<AgentSeedDefinition>(reader)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{resource}' deserialised to null — YAML file is empty or malformed.");

            if (string.IsNullOrWhiteSpace(def.AgentName))
                throw new InvalidOperationException(
                    $"Embedded resource '{resource}' is missing required field 'agent_name'.");
            if (string.IsNullOrWhiteSpace(def.AgentVersion))
                throw new InvalidOperationException(
                    $"Embedded resource '{resource}' (agent '{def.AgentName}') is missing required field 'agent_version'.");

            definitions.Add(def);
        }
        return definitions;
    }
}
