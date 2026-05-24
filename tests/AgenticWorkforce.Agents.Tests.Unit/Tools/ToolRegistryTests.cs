using AgenticWorkforce.Agents.Tools;
using AgenticWorkforce.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Tools;

public class ToolRegistryTests
{
    private static AIFunction MakeStub(string name) =>
        AIFunctionFactory.Create((string input) => input.ToUpperInvariant(), name);

    [Fact]
    public void Resolve_EmptyManifest_ReturnsEmptyList()
    {
        var registry = new ToolRegistry();

        var tools = registry.Resolve(Array.Empty<ToolBinding>());

        tools.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_UnregisteredTool_Throws()
    {
        var registry = new ToolRegistry();
        var manifest = new[] { new ToolBinding("missing.tool") };

        var act = () => registry.Resolve(manifest);

        act.Should().Throw<InvalidStateException>().WithMessage("*'missing.tool'*not registered*");
    }

    [Fact]
    public void Register_DuplicateTool_Throws()
    {
        var registry = new ToolRegistry();
        registry.Register(new ToolBinding("dup.tool"), MakeStub("dup.tool"));

        var act = () => registry.Register(new ToolBinding("dup.tool"), MakeStub("dup.tool"));

        act.Should().Throw<InvalidStateException>().WithMessage("*already registered*");
    }

    [Fact]
    public void Resolve_RequiresApproval_ThrowsBecausePhase8NotShipped()
    {
        var registry = new ToolRegistry();
        registry.Register(
            new ToolBinding("danger.tool", RequiresApproval: true),
            MakeStub("danger.tool"));

        var act = () => registry.Resolve(new[] { new ToolBinding("danger.tool", RequiresApproval: true) });

        act.Should().Throw<InvalidStateException>().WithMessage("*approval*Phase 8*");
    }

    [Fact]
    public void ToolBinding_DefaultDomain_IsSandbox()
    {
        var binding = new ToolBinding("any.tool");
        binding.Domain.Should().Be(ExecutionDomain.Sandbox);
    }
}
