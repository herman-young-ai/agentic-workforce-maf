using AgenticWorkforce.Api.Core.Auth;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Unit.Features.Projects;

public class CreateProjectTests
{
    private readonly IIdempotencyService _sut = new InMemoryIdempotencyService();

    [Fact]
    public async Task GetCachedResponseAsync_MissingKey_ReturnsNull()
    {
        var result = await _sut.GetCachedResponseAsync<string>("no-such-key");
        result.Should().BeNull();
    }

    [Fact]
    public async Task CacheResponseAsync_ThenGet_ReturnsCachedValue()
    {
        var key = Guid.NewGuid().ToString();

        await _sut.CacheResponseAsync(key, "expected");

        var result = await _sut.GetCachedResponseAsync<string>(key);
        result.Should().Be("expected");
    }

    [Fact]
    public async Task CacheResponseAsync_OverwriteExistingKey_ReturnsLatestValue()
    {
        var key = Guid.NewGuid().ToString();
        await _sut.CacheResponseAsync(key, "first");
        await _sut.CacheResponseAsync(key, "second");

        var result = await _sut.GetCachedResponseAsync<string>(key);
        result.Should().Be("second");
    }

    [Fact]
    public async Task CacheResponseAsync_DifferentKeys_AreIsolated()
    {
        var keyA = Guid.NewGuid().ToString();
        var keyB = Guid.NewGuid().ToString();
        await _sut.CacheResponseAsync(keyA, "alpha");
        await _sut.CacheResponseAsync(keyB, "beta");

        (await _sut.GetCachedResponseAsync<string>(keyA)).Should().Be("alpha");
        (await _sut.GetCachedResponseAsync<string>(keyB)).Should().Be("beta");
    }

    [Fact]
    public async Task CacheResponseAsync_SupportsComplexTypes()
    {
        var key = Guid.NewGuid().ToString();
        var payload = new { Id = Guid.NewGuid(), Name = "Test Project", Count = 42 };

        await _sut.CacheResponseAsync(key, payload);

        var result = await _sut.GetCachedResponseAsync<object>(key);
        result.Should().BeEquivalentTo(payload);
    }
}
