using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Unit.Features.Workflows;

/// <summary>
/// Pure validator tests — no DB, no DI. One test per <see cref="WorkflowValidationCause"/>
/// so author-facing error messages are wired correctly to specific rules.
/// </summary>
public class WorkflowValidatorUnitTests
{
    private readonly IWorkflowValidator _sut = new WorkflowValidator();

    [Fact]
    public void Validate_HappyPathDag_ReturnsValid()
    {
        var nodes = """
            [{"id":"s","type":"Start"},{"id":"a","type":"AgentTask"},{"id":"e","type":"End"}]
            """;
        var edges = """
            [{"from":"s","to":"a"},{"from":"a","to":"e"}]
            """;

        var result = _sut.Validate(nodes, edges);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_ZeroStartNodes_ReportsStartNodeCount()
    {
        var nodes = """[{"id":"a","type":"AgentTask"},{"id":"e","type":"End"}]""";
        var edges = """[{"from":"a","to":"e"}]""";

        var result = _sut.Validate(nodes, edges);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Cause == WorkflowValidationCause.StartNodeCount);
    }

    [Fact]
    public void Validate_TwoStartNodes_ReportsStartNodeCount()
    {
        var nodes = """
            [{"id":"s1","type":"Start"},{"id":"s2","type":"Start"},{"id":"e","type":"End"}]
            """;
        var edges = """[{"from":"s1","to":"e"},{"from":"s2","to":"e"}]""";

        var result = _sut.Validate(nodes, edges);

        result.Errors.Should().Contain(e => e.Cause == WorkflowValidationCause.StartNodeCount);
    }

    [Fact]
    public void Validate_NoEndNode_ReportsNoEndNode()
    {
        var nodes = """[{"id":"s","type":"Start"},{"id":"a","type":"AgentTask"}]""";
        var edges = """[{"from":"s","to":"a"}]""";

        var result = _sut.Validate(nodes, edges);

        result.Errors.Should().Contain(e => e.Cause == WorkflowValidationCause.NoEndNode);
    }

    [Fact]
    public void Validate_DanglingEdge_ReportsDanglingEdge()
    {
        var nodes = """
            [{"id":"s","type":"Start"},{"id":"e","type":"End"}]
            """;
        var edges = """[{"from":"s","to":"nowhere"}]""";

        var result = _sut.Validate(nodes, edges);

        result.Errors.Should().Contain(e => e.Cause == WorkflowValidationCause.DanglingEdge);
    }

    [Fact]
    public void Validate_OrphanNode_ReportsOrphanNode()
    {
        var nodes = """
            [{"id":"s","type":"Start"},{"id":"orphan","type":"AgentTask"},{"id":"e","type":"End"}]
            """;
        var edges = """[{"from":"s","to":"e"}]""";

        var result = _sut.Validate(nodes, edges);

        result.Errors.Should().Contain(e => e.Cause == WorkflowValidationCause.OrphanNode);
    }

    [Fact]
    public void Validate_DecisionWithUnlabeledEdge_ReportsDecisionEdgeMissingLabel()
    {
        var nodes = """
            [{"id":"s","type":"Start"},{"id":"d","type":"Decision"},
             {"id":"a","type":"AgentTask"},{"id":"e","type":"End"}]
            """;
        var edges = """
            [{"from":"s","to":"d"},{"from":"d","to":"a"},{"from":"a","to":"e"}]
            """;

        var result = _sut.Validate(nodes, edges);

        result.Errors.Should().Contain(e => e.Cause == WorkflowValidationCause.DecisionEdgeMissingLabel);
    }

    [Fact]
    public void Validate_DecisionWithLabeledEdges_AcceptsLabel()
    {
        var nodes = """
            [{"id":"s","type":"Start"},{"id":"d","type":"Decision"},
             {"id":"a","type":"AgentTask"},{"id":"e","type":"End"}]
            """;
        var edges = """
            [{"from":"s","to":"d"},{"from":"d","to":"a","label":"yes"},
             {"from":"d","to":"e","label":"no"},{"from":"a","to":"e"}]
            """;

        var result = _sut.Validate(nodes, edges);

        result.Errors.Should().NotContain(e => e.Cause == WorkflowValidationCause.DecisionEdgeMissingLabel);
    }

    [Fact]
    public void Validate_Cycle_ReportsCycle()
    {
        var nodes = """
            [{"id":"s","type":"Start"},{"id":"a","type":"AgentTask"},
             {"id":"b","type":"AgentTask"},{"id":"e","type":"End"}]
            """;
        var edges = """
            [{"from":"s","to":"a"},{"from":"a","to":"b"},
             {"from":"b","to":"a"},{"from":"a","to":"e"}]
            """;

        var result = _sut.Validate(nodes, edges);

        result.Errors.Should().Contain(e => e.Cause == WorkflowValidationCause.Cycle);
    }

    [Fact]
    public void Validate_MalformedNodes_ReportsMalformedJson()
    {
        var result = _sut.Validate("not json", "[]");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Cause == WorkflowValidationCause.MalformedJson);
    }
}
