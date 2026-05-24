using AgenticWorkforce.Agents.Verification;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Verification;

public class VerificationPipelineTests
{
    private static AgenticTask NewTask(Guid projectId) => new()
    {
        Id        = Guid.NewGuid(),
        ProjectId = projectId,
        Objective = "Test task",
        Status    = Domain.Enums.TaskStatus.Running
    };

    private static AgentCatalog Agent(string name, bool producesArtifact = false, string? interfaceJson = null) => new()
    {
        AgentName = name,
        AgentVersion = "1.0.0",
        ProducesArtifact = producesArtifact,
        Interface = interfaceJson
    };

    private static VerificationPipeline Build(IAgentRuntime runtime) =>
        new(new SchemaVerifier(), new RuleVerifier(), new AgentVerifier(runtime), NullLogger<VerificationPipeline>.Instance);

    [Fact]
    public async Task Pipeline_AllTiersPass_ReturnsPass()
    {
        var runtime = new ApprovingRuntime();
        var sut = Build(runtime);

        var result = await sut.VerifyAsync(
            NewTask(Guid.NewGuid()),
            output: """{ "passed": true }""",
            Agent("test.agent", producesArtifact: true,
                interfaceJson: """{"outputSchema":{"type":"object"}}"""),
            CancellationToken.None);

        result.Passed.Should().BeTrue();
        runtime.InvocationCount.Should().Be(1, "Tier 3 runs for artefact-producing agents.");
    }

    [Fact]
    public async Task Pipeline_Tier1Fails_ShortCircuits_Tier3NotCalled()
    {
        var runtime = new ApprovingRuntime();
        var sut = Build(runtime);

        var result = await sut.VerifyAsync(
            NewTask(Guid.NewGuid()),
            output: "this is not json",
            Agent("test.agent", producesArtifact: true,
                interfaceJson: """{"outputSchema":{"type":"object"}}"""),
            CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.FailedAt.Should().Be(FailureTier.Tier1Structural);
        runtime.InvocationCount.Should().Be(0, "the pipeline must short-circuit before Tier 3.");
    }

    [Fact]
    public async Task Pipeline_Tier2Fails_OnEmptyOutput_ShortCircuits()
    {
        var runtime = new ApprovingRuntime();
        var sut = Build(runtime);

        var result = await sut.VerifyAsync(
            NewTask(Guid.NewGuid()),
            output: "   ",
            Agent("test.agent", producesArtifact: true),
            CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.FailedAt.Should().Be(FailureTier.Tier2Rules);
        runtime.InvocationCount.Should().Be(0);
    }

    [Fact]
    public async Task Pipeline_NonArtifactAgent_SkipsTier3()
    {
        var runtime = new ApprovingRuntime();
        var sut = Build(runtime);

        var result = await sut.VerifyAsync(
            NewTask(Guid.NewGuid()),
            output: "Some valid prose output.",
            Agent("test.agent", producesArtifact: false),
            CancellationToken.None);

        result.Passed.Should().BeTrue();
        runtime.InvocationCount.Should().Be(0, "Tier 3 only runs for artefact-producing agents.");
    }

    [Fact]
    public async Task Pipeline_RecursionGuard_ProtectsSystemVerifier()
    {
        // Even though system.verifier declares produces_artifact in its YAML, the
        // pipeline must skip Tier 3 when the agent under review IS system.verifier
        // — otherwise verifying the verifier triggers an infinite re-entry.
        var runtime = new ApprovingRuntime();
        var sut = Build(runtime);

        var result = await sut.VerifyAsync(
            NewTask(Guid.NewGuid()),
            output: """{ "passed": true, "reason": "ok" }""",
            Agent(AgentVerifier.AgentName, producesArtifact: true),
            CancellationToken.None);

        result.Passed.Should().BeTrue();
        runtime.InvocationCount.Should().Be(0, "the recursion guard must skip Tier 3 for system.verifier itself.");
    }

    [Fact]
    public async Task Pipeline_Tier3Rejects_ReturnsFailure()
    {
        var runtime = new RejectingRuntime("Output omits remediation steps.");
        var sut = Build(runtime);

        var result = await sut.VerifyAsync(
            NewTask(Guid.NewGuid()),
            output: """{ "passed": true }""",
            Agent("test.agent", producesArtifact: true),
            CancellationToken.None);

        result.Passed.Should().BeFalse();
        result.FailedAt.Should().Be(FailureTier.Tier3Agent);
        result.Reason.Should().Contain("remediation");
    }

    // ---------- fakes ----------

    private sealed class ApprovingRuntime : IAgentRuntime
    {
        public int InvocationCount { get; private set; }
        public Task<AgentExecutionResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct = default)
        {
            InvocationCount++;
            return Task.FromResult(new AgentExecutionResult(
                Success: true,
                Output: """{ "passed": true, "reason": "looks good" }""",
                Error: null,
                InputTokens: 10, OutputTokens: 5, CostUsd: 0.0001m,
                DurationSeconds: 0.1, ToolCallCount: 0));
        }
    }

    private sealed class RejectingRuntime(string reason) : IAgentRuntime
    {
        public Task<AgentExecutionResult> ExecuteAsync(AgentExecutionRequest request, CancellationToken ct = default)
            => Task.FromResult(new AgentExecutionResult(
                Success: true,
                Output: $$"""{ "passed": false, "reason": "{{reason}}" }""",
                Error: null,
                InputTokens: 10, OutputTokens: 5, CostUsd: 0.0001m,
                DurationSeconds: 0.1, ToolCallCount: 0));
    }
}
