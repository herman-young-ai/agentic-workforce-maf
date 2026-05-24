using AgenticWorkforce.Agents.Runtime;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Runtime;

public class StubChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_ReturnsCannedTextWithUsage()
    {
        using var client = new StubChatClient();

        var response = await client.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "ping") },
            new ChatOptions { ModelId = "stub-model" });

        response.Text.Should().Contain("stub response");
        response.Usage.Should().NotBeNull();
        response.Usage!.InputTokenCount.Should().Be(100);
        response.Usage.OutputTokenCount.Should().Be(50);
    }
}
