using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AgenticWorkforce.Agents;
using FluentAssertions;
using Xunit;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace AgenticWorkforce.Architecture.Tests;

/// <summary>
/// Phase 7b architecture invariants for the seeded agent catalog YAMLs:
/// <list type="bullet">
///   <item>All 16 expected agents are present.</item>
///   <item>Every YAML parses (no malformed embedded resource).</item>
///   <item>No YAML references a Phase-8-deferred tool name (Approval-required tools
///         can't be wired until <c>ApprovalRequiredAIFunction</c> ships).</item>
///   <item>No YAML sets <c>requires_approval: true</c> on a tool binding (same reason).</item>
///   <item>Every YAML declares a non-empty <c>agent_name</c> and <c>agent_version</c>.</item>
/// </list>
/// </summary>
public class SeededYamlTests
{
    private const string ResourceInfix = ".Catalog.Seeds.";

    private static readonly Assembly AgentsAsm = typeof(AgentsAssemblyMarker).Assembly;

    private static readonly IDeserializer Yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly string[] DeferredTools =
    [
        "project.update_budget",
        "project.approve_tasks",
        "project.refine_plan",
        "project.run_workflow"
    ];

    private static readonly string[] ExpectedAgents =
    [
        "project.director", "project.planner", "project.supervisor",
        "research.strategist", "research.searcher", "research.analyst", "research.synthesizer",
        "security.webapp.scanner", "security.webapp.triage", "security.webapp.reporter",
        "software.code-analyst", "software.architecture-reviewer", "software.quality-verifier",
        "system.summarizer", "system.verifier", "system.knowledge-officer"
    ];

    private static IEnumerable<(string Resource, string Body)> LoadSeedYamls()
    {
        foreach (var resource in AgentsAsm.GetManifestResourceNames()
                     .Where(n => n.Contains(ResourceInfix, System.StringComparison.Ordinal)
                              && n.EndsWith(".yaml", System.StringComparison.OrdinalIgnoreCase))
                     .OrderBy(n => n, System.StringComparer.Ordinal))
        {
            using var stream = AgentsAsm.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            yield return (resource, reader.ReadToEnd());
        }
    }

    [Fact]
    public void AllExpectedAgents_ArePresent()
    {
        var present = LoadSeedYamls()
            .Select(s => Yaml.Deserialize<YamlAgentShape>(s.Body).AgentName)
            .OrderBy(n => n, System.StringComparer.Ordinal)
            .ToArray();

        present.Should().BeEquivalentTo(ExpectedAgents,
            "the seeder must populate every agent referenced by the platform's workflows and the test fixtures.");
    }

    [Fact]
    public void EverySeedYaml_HasNameAndVersion()
    {
        foreach (var (resource, body) in LoadSeedYamls())
        {
            var agent = Yaml.Deserialize<YamlAgentShape>(body);
            agent.AgentName.Should().NotBeNullOrWhiteSpace($"{resource} must declare agent_name");
            agent.AgentVersion.Should().NotBeNullOrWhiteSpace($"{resource} must declare agent_version");
        }
    }

    [Fact]
    public void NoSeedYaml_ReferencesAPhase8DeferredTool()
    {
        foreach (var (resource, body) in LoadSeedYamls())
        {
            var agent = Yaml.Deserialize<YamlAgentShape>(body);
            var names = agent.Tools?.Select(t => t.Name).ToArray() ?? [];
            foreach (var deferred in DeferredTools)
            {
                names.Should().NotContain(deferred,
                    $"{resource} references Phase-8-deferred tool '{deferred}' — Phase-7 manifests cannot bind approval/workflow-gated tools.");
            }
        }
    }

    [Fact]
    public void NoSeedYaml_SetsRequiresApproval()
    {
        foreach (var (resource, body) in LoadSeedYamls())
        {
            var agent = Yaml.Deserialize<YamlAgentShape>(body);
            if (agent.Tools is null) continue;
            agent.Tools.Should().NotContain(t => t.RequiresApproval,
                $"{resource} sets requires_approval=true on a tool binding — Phase 6 ToolRegistry throws on this until Phase 8 adds ApprovalRequiredAIFunction.");
        }
    }

    [Fact]
    public void EverySeedYaml_DeclaresValidSemver()
    {
        var pattern = new Regex(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);
        foreach (var (resource, body) in LoadSeedYamls())
        {
            var agent = Yaml.Deserialize<YamlAgentShape>(body);
            pattern.IsMatch(agent.AgentVersion).Should().BeTrue(
                $"{resource}: agent_version '{agent.AgentVersion}' must match Major.Minor.Patch integer triple (AgentSemver parse rule).");
        }
    }

    // ---------- minimal shape just for these tests; the real shape lives in Infrastructure ----------

    private sealed class YamlAgentShape
    {
        public string AgentName { get; set; } = string.Empty;
        public string AgentVersion { get; set; } = string.Empty;
        public List<YamlToolShape>? Tools { get; set; }
    }

    private sealed class YamlToolShape
    {
        public string Name { get; set; } = string.Empty;
        public bool RequiresApproval { get; set; }
    }
}
