using System.Threading.Channels;
using AgenticWorkforce.Agents;
using AgenticWorkforce.Agents.Runtime;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Interfaces.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AgenticWorkforce.Agents.Tests.Unit.Runtime;

public class ChatClientFactoryTests
{
    [Fact]
    public void GetOrCreate_SamePair_ReturnsSamePipelineInstance()
    {
        var channel = Channel.CreateBounded<LlmCall>(10);
        var factory = new ChatClientFactory(
            new FakeBudgetService(),
            new FakeModelPricingService(),
            new FakeTokenCounter(),
            channel.Writer,
            TimeProvider.System,
            NullLoggerFactory.Instance,
            Options.Create(new AgentRuntimeOptions()));

        var first  = factory.GetOrCreate("stub", "stub-model");
        var second = factory.GetOrCreate("stub", "stub-model");

        first.Should().BeSameAs(second, "pipelines are cached per (provider, model)");
    }

    [Fact]
    public void GetOrCreate_DifferentPair_ReturnsDifferentInstances()
    {
        var channel = Channel.CreateBounded<LlmCall>(10);
        var factory = new ChatClientFactory(
            new FakeBudgetService(),
            new FakeModelPricingService(),
            new FakeTokenCounter(),
            channel.Writer,
            TimeProvider.System,
            NullLoggerFactory.Instance,
            Options.Create(new AgentRuntimeOptions()));

        var a = factory.GetOrCreate("stub", "model-a");
        var b = factory.GetOrCreate("stub", "model-b");

        a.Should().NotBeSameAs(b);
    }

    private sealed class FakeBudgetService : IBudgetService
    {
        public Task<bool> CanSpendAsync(Guid p, Guid? s, decimal c, CancellationToken ct = default) => Task.FromResult(true);
        public Task RecordSpendAsync(Guid p, Guid? s, Guid? t, decimal c, CancellationToken ct = default) => Task.CompletedTask;
        public Task<BudgetStatus> GetStatusAsync(Guid p, CancellationToken ct = default) =>
            Task.FromResult(new BudgetStatus(0, 0, 0, false));
    }

    private sealed class FakeModelPricingService : IModelPricingService
    {
        public Task<decimal> EstimateInputCostAsync(string m, int i, CancellationToken ct = default) => Task.FromResult(0m);
        public Task<decimal> CalculateCostAsync(string m, long i, long o, long cr, long cc, CancellationToken ct = default) => Task.FromResult(0m);
    }

    private sealed class FakeTokenCounter : ITokenCounter
    {
        public Task<int> CountAsync(string text, string modelId, CancellationToken ct = default) => Task.FromResult(text.Length);
    }
}
