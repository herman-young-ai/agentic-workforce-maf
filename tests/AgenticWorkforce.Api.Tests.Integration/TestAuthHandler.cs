using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AgenticWorkforce.Api.Tests.Integration;

/// <summary>
/// Replaces JWT validation in the Testing environment.
/// Tests set X-Test-User-Id and X-Test-User-Roles headers on the HttpClient.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "TestAuth";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-User-Id", out var userIdHeader)
            || !Guid.TryParse(userIdHeader.ToString(), out _))
            return Task.FromResult(AuthenticateResult.NoResult());

        var userId = userIdHeader.ToString();
        var email  = Request.Headers.TryGetValue("X-Test-User-Email", out var emailHeader)
            ? emailHeader.ToString()
            : $"user-{userId}@test.local";

        var rolesRaw = Request.Headers.TryGetValue("X-Test-User-Roles", out var rolesHeader)
            ? rolesHeader.ToString()
            : string.Empty;

        var claims = new List<Claim>
        {
            new("oid",                       userId),
            new(ClaimTypes.NameIdentifier,   userId),
            new(ClaimTypes.Email,            email),
            new("preferred_username",        email),
            new("name",                      email)
        };

        foreach (var role in rolesRaw.Split(',', StringSplitOptions.RemoveEmptyEntries))
            claims.Add(new Claim(ClaimTypes.Role, role.Trim()));

        var identity  = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket    = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
