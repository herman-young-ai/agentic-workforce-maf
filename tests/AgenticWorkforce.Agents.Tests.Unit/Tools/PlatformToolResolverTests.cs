using AgenticWorkforce.Agents.Tools;
using AgenticWorkforce.Domain.Agents;
using AgenticWorkforce.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Tools;

public class PlatformToolResolverTests
{
    private static IServiceProvider EmptyServices() =>
        new ServiceCollection().BuildServiceProvider();

    [Fact]
    public void Resolve_KnownBinding_ReturnsAiTool()
    {
        var sut = new PlatformToolResolver(EmptyServices(), new IPlatformToolFactory[] { new FakeFactory("project.fake") });

        var tools = sut.Resolve(
            new[] { new AgentToolBindingShape("project.fake") },
            Guid.NewGuid());

        tools.Should().HaveCount(1);
        tools[0].Name.Should().Be("project.fake");
    }

    [Fact]
    public void Resolve_UnknownBinding_IsSkipped()
    {
        var sut = new PlatformToolResolver(EmptyServices(), new IPlatformToolFactory[] { new FakeFactory("project.fake") });

        var tools = sut.Resolve(
            new[]
            {
                new AgentToolBindingShape("project.fake"),
                new AgentToolBindingShape("web.search")     // sandbox tool — handled elsewhere
            },
            Guid.NewGuid());

        tools.Should().HaveCount(1, "unknown bindings are not platform tools; the sandbox / MCP path handles them.");
    }

    [Fact]
    public void Resolve_RequiresApprovalBinding_Throws()
    {
        var sut = new PlatformToolResolver(EmptyServices(), Array.Empty<IPlatformToolFactory>());

        var act = () => sut.Resolve(
            new[] { new AgentToolBindingShape("anything", RequiresApproval: true) },
            Guid.NewGuid());

        act.Should().Throw<InvalidStateException>().WithMessage("*requires human approval*");
    }

    [Fact]
    public void Resolve_DistinctFactoriesPerBinding_AllRegistered()
    {
        var factories = new IPlatformToolFactory[]
        {
            new FakeFactory("a"),
            new FakeFactory("b"),
            new FakeFactory("c")
        };
        var sut = new PlatformToolResolver(EmptyServices(), factories);

        var tools = sut.Resolve(
            new[] { new AgentToolBindingShape("a"), new AgentToolBindingShape("c") },
            Guid.NewGuid());

        tools.Select(t => t.Name).Should().BeEquivalentTo(new[] { "a", "c" });
    }

    private sealed class FakeFactory(string name) : IPlatformToolFactory
    {
        public string ToolName => name;
        public AITool Create(IServiceProvider services, Guid projectId) =>
            AIFunctionFactory.Create((CancellationToken ct) => Task.FromResult("ok"), new AIFunctionFactoryOptions { Name = name });
    }
}
