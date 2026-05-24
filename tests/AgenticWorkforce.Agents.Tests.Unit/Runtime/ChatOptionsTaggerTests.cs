using AgenticWorkforce.Agents.Runtime;
using AgenticWorkforce.Domain.Exceptions;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Runtime;

public class ChatOptionsTaggerTests
{
    private static readonly AgentExecutionContext SampleContext = new(
        ProjectId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        TaskId:    Guid.Parse("22222222-2222-2222-2222-222222222222"),
        SessionId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
        AgentName: "test.agent",
        Objective: "do the thing",
        Input:     null);

    [Fact]
    public void Apply_Then_Read_Roundtrips_All_Fields()
    {
        var options = ChatOptionsTagger.Apply(
            new ChatOptions(), SampleContext, agentRole: "Reviewer", provider: "stub", requestId: "req-1");

        var read = ChatOptionsTagger.Read(options);

        read.ProjectId.Should().Be(SampleContext.ProjectId);
        read.TaskId.Should().Be(SampleContext.TaskId);
        read.SessionId.Should().Be(SampleContext.SessionId);
        read.AgentName.Should().Be("test.agent");
        read.AgentRole.Should().Be("Reviewer");
        read.Provider.Should().Be("stub");
        read.RequestId.Should().Be("req-1");
    }

    [Fact]
    public void Read_WithMissingTags_ThrowsInvalidStateException()
    {
        var act = () => ChatOptionsTagger.Read(new ChatOptions());
        act.Should().Throw<InvalidStateException>();
    }

    [Fact]
    public void Read_WithNullOptions_ThrowsInvalidStateException()
    {
        var act = () => ChatOptionsTagger.Read(null);
        act.Should().Throw<InvalidStateException>();
    }
}
