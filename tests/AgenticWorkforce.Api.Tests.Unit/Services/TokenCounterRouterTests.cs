using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Unit.Services;

public class TokenCounterRouterTests
{
    private static TokenCounterRouter MakeSut() =>
        new(new TiktokenTokenCounter(), new AnthropicTokenCounter());

    [Theory]
    [InlineData("gpt-4o")]
    [InlineData("gpt-4")]
    [InlineData("gpt-5-mini")]
    [InlineData("o1-mini")]
    [InlineData("o3-pro")]
    [InlineData("text-embedding-3-large")]
    public async Task RoutesOpenAiFamily(string model)
    {
        var sut = MakeSut();
        var count = await sut.CountAsync("hello world", model);
        count.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData("claude-sonnet-4-6")]
    [InlineData("claude-opus-4-7")]
    public async Task RoutesClaudeFamily(string model)
    {
        var sut = MakeSut();
        var count = await sut.CountAsync("hello world", model);
        count.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task UnknownModel_Throws()
    {
        var sut = MakeSut();
        var act = async () => await sut.CountAsync("hi", "some-mystery-model");
        await act.Should().ThrowAsync<InvalidStateException>().WithMessage("*no tokenizer registered*");
    }

    [Fact]
    public async Task AnthropicCounter_UsesCharBasedApproximation()
    {
        var sut = new AnthropicTokenCounter();
        var count = await sut.CountAsync("12345678", "claude-sonnet-4-6"); // 8 chars / 4 = 2
        count.Should().Be(2);
    }
}
