using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Unit.Services;

public class ModelPricingServiceTests
{
    private static readonly DateTime Now = new(2026, 5, 23, 12, 0, 0, DateTimeKind.Utc);
    private static readonly ModelPricing Sample = new()
    {
        Model = "stub-model",
        EffectiveFrom = Now.AddDays(-10),
        EffectiveTo = null,
        PricePerMtokInput        = 3m,    // $3 / Mtok input
        PricePerMtokOutput       = 15m,   // $15 / Mtok output
        PricePerMtokCacheRead    = 0.3m,
        PricePerMtokCacheCreate  = 3.75m
    };

    [Fact]
    public async Task EstimateInputCostAsync_LinearInTokens()
    {
        var clock = new FixedTimeProvider(Now);
        var sut = new ModelPricingService(new FakeRepo(Sample), clock);

        var cost = await sut.EstimateInputCostAsync("stub-model", inputTokens: 100_000);

        cost.Should().Be(0.3m); // 100k tokens * $3 / Mtok = $0.30
    }

    [Fact]
    public async Task CalculateCostAsync_SumsAllFourTokenClasses()
    {
        var clock = new FixedTimeProvider(Now);
        var sut = new ModelPricingService(new FakeRepo(Sample), clock);

        var cost = await sut.CalculateCostAsync(
            "stub-model",
            input:       100_000,  // 0.30
            output:       10_000,  // 0.15
            cacheRead:   500_000,  // 0.15
            cacheCreate:  20_000); // 0.075

        cost.Should().Be(0.675m);
    }

    [Fact]
    public async Task NoMatchingRow_ThrowsInvalidState()
    {
        var clock = new FixedTimeProvider(Now);
        var sut = new ModelPricingService(new FakeRepo(null), clock);

        var act = async () => await sut.EstimateInputCostAsync("nope", 100);

        await act.Should().ThrowAsync<InvalidStateException>().WithMessage("*No ModelPricing row*");
    }

    private sealed class FakeRepo(ModelPricing? row) : IModelPricingRepository
    {
        public Task<ModelPricing?> GetEffectiveAsync(string model, DateTime atUtc, CancellationToken ct = default)
            => Task.FromResult(row?.Model == model ? row : null);
    }

    private sealed class FixedTimeProvider(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }
}
