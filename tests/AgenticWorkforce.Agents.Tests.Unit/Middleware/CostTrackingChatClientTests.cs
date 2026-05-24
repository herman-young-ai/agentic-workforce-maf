using System.Threading.Channels;
using AgenticWorkforce.Agents.Middleware;
using AgenticWorkforce.Agents.Runtime;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Middleware;

public class CostTrackingChatClientTests
{
    private static readonly AgentExecutionContext Ctx = new(
        ProjectId: Guid.NewGuid(), TaskId: Guid.NewGuid(), SessionId: null,
        AgentName: "test.agent", Objective: "x", Input: null);

    private static ChatOptions TaggedOptions(string model = "stub-model") =>
        ChatOptionsTagger.Apply(new ChatOptions { ModelId = model }, Ctx, agentRole: null, provider: "stub", requestId: null);

    [Fact]
    public async Task GetResponseAsync_WritesOneLlmCallRecord_WithAllTokenClasses()
    {
        var channel = Channel.CreateBounded<LlmCall>(10);
        var pricing = new FakePricing(perCallCost: 0.0042m);
        var inner = new StubChatClient();
        var sut = new CostTrackingChatClient(inner, pricing, channel.Writer, TimeProvider.System);

        var response = await sut.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hi") },
            TaggedOptions());

        response.Text.Should().Contain("stub response");

        channel.Reader.TryRead(out var record).Should().BeTrue();
        record!.Model.Should().Be("stub-model");
        record.Provider.Should().Be("stub");
        record.ProjectId.Should().Be(Ctx.ProjectId);
        record.InputTokens.Should().Be(100);
        record.OutputTokens.Should().Be(50);
        record.CostUsd.Should().Be(0.0042m);
        record.AgentName.Should().Be("test.agent");
    }

    [Fact]
    public async Task GetResponseAsync_FullChannel_ThrowsAuditBackpressure()
    {
        // BoundedChannelFullMode.Wait causes TryWrite to return false when full
        // (WriteAsync would block; the middleware uses TryWrite so it sees the failure).
        var channel = Channel.CreateBounded<LlmCall>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.Wait });
        channel.Writer.TryWrite(new LlmCall { Model = "x", Provider = "x" }).Should().BeTrue("first write fills the bound");
        // Reader hasn't consumed, channel is full.
        var sut = new CostTrackingChatClient(new StubChatClient(), new FakePricing(0m), channel.Writer, TimeProvider.System);

        var act = async () => await sut.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hi") }, TaggedOptions());

        await act.Should().ThrowAsync<AuditBackpressureException>();
    }

    [Fact]
    public async Task GetResponseAsync_MissingModelId_Throws()
    {
        var sut = new CostTrackingChatClient(new StubChatClient(), new FakePricing(0m),
            Channel.CreateBounded<LlmCall>(10).Writer, TimeProvider.System);

        var act = async () => await sut.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hi") },
            ChatOptionsTagger.Apply(new ChatOptions(), Ctx, null, "stub", null));

        await act.Should().ThrowAsync<InvalidStateException>().WithMessage("*ModelId is required*");
    }

    private sealed class FakePricing(decimal perCallCost) : IModelPricingService
    {
        public Task<decimal> EstimateInputCostAsync(string m, int i, CancellationToken ct = default) => Task.FromResult(perCallCost);
        public Task<decimal> CalculateCostAsync(string m, long i, long o, long cr, long cc, CancellationToken ct = default) => Task.FromResult(perCallCost);
    }
}
