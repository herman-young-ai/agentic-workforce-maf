using System.Linq;
using System.Reflection;
using AgenticWorkforce.Agents;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Catalog;

/// <summary>
/// Phase 7a anchor: at least one seed YAML is embedded under the canonical
/// resource path the seeder scans. Later sub-phases add the remaining 15
/// YAMLs and the YAML-shape parsing tests; this test pins the assembly
/// layout so a packaging slip is loud.
/// </summary>
public class EmbeddedYamlSeedAnchorTests
{
    [Fact]
    public void AgentsAssembly_ExposesAtLeastOneSeedYaml()
    {
        var asm = typeof(AgentsAssemblyMarker).Assembly;
        var seedResources = asm.GetManifestResourceNames()
            .Where(n => n.Contains(".Catalog.Seeds.", System.StringComparison.Ordinal)
                     && n.EndsWith(".yaml", System.StringComparison.OrdinalIgnoreCase))
            .ToList();

        seedResources.Should().NotBeEmpty(
            "AgenticWorkforce.Agents.csproj must <EmbeddedResource Include=\"Catalog/Seeds/*.yaml\" />.");
        seedResources.Should().Contain(n => n.EndsWith("system.verifier.yaml", System.StringComparison.Ordinal),
            "the system.verifier proof YAML is the Phase 7a anchor — required for the verification pipeline's recursion guard test in 7e.");
    }
}
