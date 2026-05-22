using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for personal API keys. All read/write paths are user-scoped:
/// callers pass the requesting user id so the repository can enforce ownership
/// without leaking key rows across users.
/// </summary>
public interface IApiKeyRepository
{
    Task<ApiKey> AddAsync(ApiKey key, CancellationToken ct = default);

    Task<IReadOnlyList<ApiKey>> ListByUserAsync(Guid userId, CancellationToken ct = default);

    Task<ApiKey?> GetByIdForUserAsync(Guid keyId, Guid userId, CancellationToken ct = default);

    /// <summary>
    /// Soft delete by setting <see cref="ApiKey.RevokedAt"/>. Returns false if
    /// no matching key exists for the user (already revoked or never existed).
    /// </summary>
    Task<bool> RevokeAsync(Guid keyId, Guid userId, CancellationToken ct = default);
}
