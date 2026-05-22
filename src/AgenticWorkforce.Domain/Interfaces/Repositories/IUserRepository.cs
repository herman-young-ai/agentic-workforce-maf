using AgenticWorkforce.Domain.Entities;

namespace AgenticWorkforce.Domain.Interfaces.Repositories;

/// <summary>
/// Repository for the User aggregate. Includes JIT provisioning since a user's
/// platform record is created on first successful login (Entra ID is the
/// identity provider; the local row carries the platform-specific fields).
/// </summary>
public interface IUserRepository
{
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task UpdateAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// Returns the existing user record or creates one if none exists. Used by
    /// `/api/v1/auth/me` to ensure first-login JIT provisioning. Returns true
    /// in the second tuple slot when a new record was created.
    /// </summary>
    Task<(User User, bool Created)> EnsureProvisionedAsync(
        Guid id,
        string email,
        string displayName,
        CancellationToken ct = default);
}
