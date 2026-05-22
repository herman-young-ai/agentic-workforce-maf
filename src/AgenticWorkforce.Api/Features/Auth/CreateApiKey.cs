using System.Security.Cryptography;
using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Exceptions;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace AgenticWorkforce.Api.Features.Auth;

public static class CreateApiKey
{
    public record Request(string Name, DateTime? ExpiresAt = null, string? Scopes = null);

    public record Response(
        Guid Id,
        string Name,
        string Key,
        string KeyPrefix,
        DateTime? ExpiresAt,
        DateTime CreatedAt);

    public static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapPost("/api/v1/auth/me/api-keys", HandleAsync)
            .RequireAuthorization()
            .WithTags("Auth")
            ;

    private static async Task<IResult> HandleAsync(
        [FromBody] Request request,
        ICurrentUserAccessor userAccessor,
        IUserRepository users,
        IApiKeyRepository apiKeys,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ValidationException("API key name is required.");

        var user = userAccessor.User;

        if (await users.GetByIdAsync(user.Id, ct) is null)
            throw new NotFoundException("User", user.Id);

        var rawKey = GenerateApiKey();
        var prefix = rawKey[..12];
        var hashedKey = HashKey(rawKey);

        var apiKey = await apiKeys.AddAsync(new ApiKey
        {
            UserId    = user.Id,
            Name      = request.Name,
            KeyPrefix = prefix,
            HashedKey = hashedKey,
            ExpiresAt = request.ExpiresAt,
            Scopes    = request.Scopes
        }, ct);

        // Return the full key once — it cannot be retrieved again
        return Results.Created($"/api/v1/auth/me/api-keys/{apiKey.Id}",
            new Response(apiKey.Id, apiKey.Name, rawKey, prefix, apiKey.ExpiresAt, apiKey.CreatedAt));
    }

    private static string GenerateApiKey()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "awf_" + Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    private static string HashKey(string rawKey)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
