using AgenticWorkforce.Agents.Middleware;
using AgenticWorkforce.Agents.Runtime;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Services;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Middleware;

public class BudgetEnforcingChatClientTests
{
    private static readonly AgentExecutionContext Ctx = new(
        ProjectId: Guid.NewGuid(), TaskId: Guid.NewGuid(), SessionId: null,
        AgentName: "test.agent", Objective: "x", Input: null);

    private static ChatOptions Tagged() => ChatOptionsTagger.Apply(
        new ChatOptions { ModelId = "stub-model" }, Ctx, null, "stub", null);

    [Fact]
    public async Task PreCheckMode_BudgetExhausted_ThrowsBudgetExceeded()
    {
        var sut = new BudgetEnforcingChatClient(
            new StubChatClient(),
            new FakeBudget(canSpend: false),
            new FakePricing(),
            new FakeTokens(),
            BudgetClientMode.PreCheckOnly);

        var act = async () => await sut.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hi") }, Tagged());

        await act.Should().ThrowAsync<BudgetExceededException>();
    }

    [Fact]
    public async Task PreCheckMode_BudgetOk_Delegates()
    {
        var sut = new BudgetEnforcingChatClient(
            new StubChatClient(),
            new FakeBudget(canSpend: true),
            new FakePricing(),
            new FakeTokens(),
            BudgetClientMode.PreCheckOnly);

        var response = await sut.GetResponseAsync(
            new[] { new ChatMessage(ChatRole.User, "hi") }, Tagged());

        response.Text.Should().Contain("stub response");
    }

    [Fact]
    public async Task RecordSpendMode_RecordsActualSpendWithTaskId()
    {
        var budget = new FakeBudget(canSpend: true);
        var sut = new BudgetEnforcingChatClient(
            new StubChatClient(), budget, new FakePricing(perCall: 0.012m), new FakeTokens(),
            BudgetClientMode.RecordSpend);

        await sut.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "hi") }, Tagged());

        budget.RecordedSpend.Should().HaveCount(1);
        budget.RecordedSpend[0].ProjectId.Should().Be(Ctx.ProjectId);
        budget.RecordedSpend[0].TaskId.Should().Be(Ctx.TaskId);
        budget.RecordedSpend[0].CostUsd.Should().Be(0.012m);
    }

    private sealed class FakeBudget(bool canSpend) : IBudgetService
    {
        public List<(Guid ProjectId, Guid? SessionId, Guid? TaskId, decimal CostUsd)> RecordedSpend { get; } = new();
        public Task<bool> CanSpendAsync(Guid p, Guid? s, decimal c, CancellationToken ct = default) => Task.FromResult(canSpend);
        public Task RecordSpendAsync(Guid p, Guid? s, Guid? t, decimal c, CancellationToken ct = default)
        {
            RecordedSpend.Add((p, s, t, c));
            return Task.CompletedTask;
        }
        public Task<BudgetStatus> GetStatusAsync(Guid p, CancellationToken ct = default) =>
            Task.FromResult(new BudgetStatus(100, 0, 100, false));
    }

    private sealed class FakePricing(decimal perCall = 0m) : IModelPricingService
    {
        public Task<decimal> EstimateInputCostAsync(string m, int i, CancellationToken ct = default) => Task.FromResult(perCall);
        public Task<decimal> CalculateCostAsync(string m, long i, long o, long cr, long cc, CancellationToken ct = default) => Task.FromResult(perCall);
    }

    private sealed class FakeTokens : ITokenCounter
    {
        public Task<int> CountAsync(string text, string modelId, CancellationToken ct = default) => Task.FromResult(text.Length);
    }
}
