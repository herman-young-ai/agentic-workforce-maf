using AgenticWorkforce.Api.Core.Auth;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Unit.Features.Projects;

public class CreateProjectTests
{
    private readonly IIdempotencyService _sut = new InMemoryIdempotencyService();
    private readonly Guid _userA = Guid.NewGuid();
    private readonly Guid _userB = Guid.NewGuid();

    [Fact]
    public async Task GetCachedResponseAsync_MissingKey_ReturnsNull()
    {
        var result = await _sut.GetCachedResponseAsync<string>(_userA, "no-such-key");
        result.Should().BeNull();
    }

    [Fact]
    public async Task CacheResponseAsync_ThenGet_ReturnsCachedValue()
    {
        var key = Guid.NewGuid().ToString();

        await _sut.CacheResponseAsync(_userA, key, "expected");

        var result = await _sut.GetCachedResponseAsync<string>(_userA, key);
        result.Should().Be("expected");
    }

    [Fact]
    public async Task CacheResponseAsync_OverwriteExistingKey_ReturnsLatestValue()
    {
        var key = Guid.NewGuid().ToString();
        await _sut.CacheResponseAsync(_userA, key, "first");
        await _sut.CacheResponseAsync(_userA, key, "second");

        var result = await _sut.GetCachedResponseAsync<string>(_userA, key);
        result.Should().Be("second");
    }

    [Fact]
    public async Task CacheResponseAsync_DifferentKeys_AreIsolated()
    {
        var keyA = Guid.NewGuid().ToString();
        var keyB = Guid.NewGuid().ToString();
        await _sut.CacheResponseAsync(_userA, keyA, "alpha");
        await _sut.CacheResponseAsync(_userA, keyB, "beta");

        (await _sut.GetCachedResponseAsync<string>(_userA, keyA)).Should().Be("alpha");
        (await _sut.GetCachedResponseAsync<string>(_userA, keyB)).Should().Be("beta");
    }

    [Fact]
    public async Task CacheResponseAsync_SupportsComplexTypes()
    {
        var key = Guid.NewGuid().ToString();
        var payload = new { Id = Guid.NewGuid(), Name = "Test Project", Count = 42 };

        await _sut.CacheResponseAsync(_userA, key, payload);

        var result = await _sut.GetCachedResponseAsync<object>(_userA, key);
        result.Should().BeEquivalentTo(payload);
    }

    // -------------------------------------------------------------------------
    // Phase 3.5: cross-user isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CacheResponseAsync_CrossUser_DoesNotLeakWithSameKey()
    {
        // The pre-Phase-3.5 implementation keyed only on the raw header value, so
        // a user could submit another user's idempotency key and receive the
        // cached response (including resource Location). This test asserts that
        // (userId, key) is the only valid lookup tuple.
        var key = "shared-key";

        await _sut.CacheResponseAsync(_userA, key, "user-A-secret");

        var crossUser = await _sut.GetCachedResponseAsync<string>(_userB, key);

        crossUser.Should().BeNull(
            "user B must not see user A's cached idempotency response");
    }

    [Fact]
    public async Task CacheResponseAsync_SameKey_DifferentUsers_KeepsBothEntries()
    {
        var key = "concurrent-key";

        await _sut.CacheResponseAsync(_userA, key, "alpha");
        await _sut.CacheResponseAsync(_userB, key, "beta");

        (await _sut.GetCachedResponseAsync<string>(_userA, key)).Should().Be("alpha");
        (await _sut.GetCachedResponseAsync<string>(_userB, key)).Should().Be("beta");
    }
}
