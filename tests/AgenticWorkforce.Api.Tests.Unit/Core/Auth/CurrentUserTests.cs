using System.Security.Claims;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Exceptions;
using FluentAssertions;
using Xunit;

namespace AgenticWorkforce.Api.Tests.Unit.Core.Auth;

public class CurrentUserTests
{
    private const string ObjectId  = "10101010-1010-1010-1010-101010101010";
    private const string Email     = "user@example.com";
    private const string Display   = "Test User";

    [Fact]
    public void FromClaimsPrincipal_AllClaimsPresent_ReturnsPopulatedUser()
    {
        var principal = Build(("oid", ObjectId), (ClaimTypes.Email, Email), ("name", Display));

        var user = CurrentUser.FromClaimsPrincipal(principal);

        user.Id.Should().Be(Guid.Parse(ObjectId));
        user.Email.Should().Be(Email);
        user.DisplayName.Should().Be(Display);
    }

    [Fact]
    public void FromClaimsPrincipal_NoOidClaim_ThrowsTokenInvalid()
    {
        var principal = Build((ClaimTypes.Email, Email), ("name", Display));

        var act = () => CurrentUser.FromClaimsPrincipal(principal);

        act.Should().Throw<TokenInvalidException>()
            .WithMessage("*object-identifier claim*");
    }

    [Fact]
    public void FromClaimsPrincipal_OidIsNotAGuid_ThrowsTokenInvalid()
    {
        var principal = Build(("oid", "not-a-guid"), (ClaimTypes.Email, Email), ("name", Display));

        var act = () => CurrentUser.FromClaimsPrincipal(principal);

        act.Should().Throw<TokenInvalidException>()
            .WithMessage("*not a valid Guid*");
    }

    [Fact]
    public void FromClaimsPrincipal_OidIsEmptyGuid_ThrowsTokenInvalid()
    {
        var principal = Build(("oid", Guid.Empty.ToString()), (ClaimTypes.Email, Email), ("name", Display));

        var act = () => CurrentUser.FromClaimsPrincipal(principal);

        act.Should().Throw<TokenInvalidException>()
            .WithMessage("*empty Guid*");
    }

    [Fact]
    public void FromClaimsPrincipal_NoEmailClaim_ThrowsTokenInvalid()
    {
        var principal = Build(("oid", ObjectId), ("name", Display));

        var act = () => CurrentUser.FromClaimsPrincipal(principal);

        act.Should().Throw<TokenInvalidException>()
            .WithMessage("*email-equivalent claim*");
    }

    [Fact]
    public void FromClaimsPrincipal_NoNameClaim_ThrowsTokenInvalid()
    {
        var principal = Build(("oid", ObjectId), (ClaimTypes.Email, Email));

        var act = () => CurrentUser.FromClaimsPrincipal(principal);

        act.Should().Throw<TokenInvalidException>()
            .WithMessage("*display-name claim*");
    }

    [Theory]
    [InlineData("oid")]
    [InlineData("http://schemas.microsoft.com/identity/claims/objectidentifier")]
    [InlineData("uid")]
    [InlineData(ClaimTypes.NameIdentifier)]
    public void FromClaimsPrincipal_AcceptsAnyOidEquivalentClaim(string claimType)
    {
        var principal = Build((claimType, ObjectId), (ClaimTypes.Email, Email), ("name", Display));

        var user = CurrentUser.FromClaimsPrincipal(principal);

        user.Id.Should().Be(Guid.Parse(ObjectId));
    }

    private static ClaimsPrincipal Build(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.Type, c.Value)),
            authenticationType: "TestScheme");
        return new ClaimsPrincipal(identity);
    }
}
