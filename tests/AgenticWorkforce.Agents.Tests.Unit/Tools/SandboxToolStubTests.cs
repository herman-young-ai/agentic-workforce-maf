using AgenticWorkforce.Agents.Tools;
using AgenticWorkforce.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Tools;

/// <summary>
/// Phase 7d invariants for every sandbox tool stub: invocation throws
/// <see cref="SandboxUnavailableException"/> (no silent placeholder strings),
/// every stub is registered with the singleton <see cref="IToolRegistry"/>,
/// and the tool name on the registered binding matches the constant on the
/// stub class.
/// </summary>
public class SandboxToolStubTests
{
    private static readonly string[] AllSandboxToolNames =
    [
        "file.read", "file.write", "file.search", "shell.execute",
        "web.search", "web.fetch",
        "security.code.scan", "security.deps.scan", "security.secrets.scan", "security.vuln.lookup",
        "research.web.search", "research.extract", "research.source.evaluate",
        "software.code.analyze", "software.arch.map", "software.test.run"
    ];

    private static IToolRegistry BuildRegistry()
    {
        var sp = new ServiceCollection()
            .AddAgentTools()
            .BuildServiceProvider();
        return sp.GetRequiredService<IToolRegistry>();
    }

    [Fact]
    public void Registry_HasEverySandboxStub()
    {
        var registry = BuildRegistry();

        var resolved = registry.Resolve(
            AllSandboxToolNames.Select(n => new ToolBinding(n)).ToArray());

        resolved.Should().HaveCount(AllSandboxToolNames.Length);
        resolved.Select(t => t.Name).Should().BeEquivalentTo(AllSandboxToolNames);
    }

    [Theory]
    [InlineData("file.read")]
    [InlineData("file.write")]
    [InlineData("shell.execute")]
    [InlineData("web.search")]
    [InlineData("security.code.scan")]
    [InlineData("research.web.search")]
    [InlineData("software.test.run")]
    public async Task InvokingStub_ThrowsSandboxUnavailable(string toolName)
    {
        var registry = BuildRegistry();
        var tool = (AIFunction)registry.Resolve(new[] { new ToolBinding(toolName) }).Single();

        // Build a minimal arguments object: every required parameter on the stubs we
        // exercise is a string, so a dummy "x" satisfies the schema. Optional params are
        // left unset — the stub throws before the schema even matters.
        var args = new AIFunctionArguments();
        if (tool.JsonSchema.TryGetProperty("required", out var required))
        {
            foreach (var name in required.EnumerateArray())
                args[name.GetString()!] = "x";
        }

        var act = async () => await tool.InvokeAsync(args);
        await act.Should().ThrowAsync<SandboxUnavailableException>()
            .WithMessage($"*'{toolName}'*not yet available*");
    }
}
