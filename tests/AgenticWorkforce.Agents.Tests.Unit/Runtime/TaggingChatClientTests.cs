using AgenticWorkforce.Agents.Runtime;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Runtime;

public class TaggingChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_StampsAwpTagsBeforeDelegating()
    {
        var ctx = new AgentExecutionContext(
            ProjectId: Guid.NewGuid(),
            TaskId:    Guid.NewGuid(),
            SessionId: Guid.NewGuid(),
            AgentName: "test.agent",
            Objective: "x",
            Input:     null);

        ChatOptions? observed = null;
        var inner = new CapturingChatClient(opts => observed = opts);
        var sut = new TaggingChatClient(inner, ctx, provider: "stub", agentRole: "Reviewer");

        await sut.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") }, options: null);

        observed.Should().NotBeNull();
        var tags = ChatOptionsTagger.Read(observed);
        tags.ProjectId.Should().Be(ctx.ProjectId);
        tags.TaskId.Should().Be(ctx.TaskId);
        tags.AgentName.Should().Be("test.agent");
        tags.AgentRole.Should().Be("Reviewer");
        tags.Provider.Should().Be("stub");
    }

    private sealed class CapturingChatClient(Action<ChatOptions?> capture) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
        {
            capture(o);
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }
        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default)
            => AsyncEnumerable.Empty<ChatResponseUpdate>();
        public object? GetService(Type t, object? key = null) => null;
        public void Dispose() { }
    }
}
