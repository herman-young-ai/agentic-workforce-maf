using System.Reflection;
using AgenticWorkforce.Agents.Runtime;
using AgenticWorkforce.Agents.Tools;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace AgenticWorkforce.Architecture.Tests;

public class ModuleBoundaryTests
{
    private static readonly Assembly DomainAsm = typeof(IAgentRuntime).Assembly;
    private static readonly Assembly AgentsAsm = typeof(AgentRuntime).Assembly;
    // Touched at assembly load time to keep Infrastructure in the test boundary even though
    // no test below directly asserts against it — boundary tests for Infrastructure can be
    // added here without changing the project references.
    private static readonly Assembly InfrastructureAsm = typeof(InfrastructureServiceExtensions).Assembly;
    static ModuleBoundaryTests() { _ = InfrastructureAsm; }

    [Fact]
    public void Domain_HasNoEfCoreNpgsqlOrMafDependency()
    {
        var result = Types.InAssembly(DomainAsm)
            .Should()
            .NotHaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Microsoft.Extensions.AI",
                "Microsoft.Extensions.AI.Abstractions",
                "Microsoft.Agents.AI",
                "Microsoft.Agents.AI.Abstractions",
                "StackExchange.Redis",
                "Azure.Storage.Blobs")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain must stay free of EF Core, Npgsql, MAF, Redis, and Azure SDK dependencies (Principle 4). " +
            "Failing types: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void Agents_HasNoEfCoreOrNpgsqlDependency()
    {
        var result = Types.InAssembly(AgentsAsm)
            .Should()
            .NotHaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Agents must access persistence only through Domain repositories — no direct EF Core or Npgsql refs. " +
            "Failing types: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void PlatformTools_DoNotDependOnHttpOrFileOrProcess()
    {
        // Identifies any Platform-domain tool (IPlatformTool) and asserts it does not pull in
        // HTTP, filesystem, or process APIs. Sandboxed tools go through Dynamic Sessions; in-process
        // Platform tools are restricted to internal DB reads through repository interfaces.
        var platformTools = Types.InAssembly(AgentsAsm).That().ImplementInterface(typeof(IPlatformTool)).GetTypes().ToList();
        if (platformTools.Count == 0) return; // No Platform tools registered yet (Phase 7+).

        var result = Types.InAssembly(AgentsAsm)
            .That().ImplementInterface(typeof(IPlatformTool))
            .Should()
            .NotHaveDependencyOnAny(
                "System.Net.Http",
                "System.IO.File",
                "System.Diagnostics.Process")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "IPlatformTool implementations must not use HttpClient, File, or Process (Principle 22). " +
            "Failing types: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }

    [Fact]
    public void ChatOptionsKeys_ReferencedOnlyByTaggerInAgents()
    {
        // Whoever needs to read/write awp.* tags goes through ChatOptionsTagger.
        // No other type in Agents should reference ChatOptionsKeys directly.
        var keysName = typeof(ChatOptionsKeys).FullName!;
        var result = Types.InAssembly(AgentsAsm)
            .That().DoNotHaveName(typeof(ChatOptionsTagger).Name)
            .And().DoNotHaveName(typeof(ChatOptionsKeys).Name)
            .Should()
            .NotHaveDependencyOn(keysName)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Only ChatOptionsTagger should reference ChatOptionsKeys; other code must use the Tagger helper. " +
            "Failing types: " + string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>()));
    }
}
