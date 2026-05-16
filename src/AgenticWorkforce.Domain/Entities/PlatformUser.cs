namespace AgenticWorkforce.Domain.Entities;

/// <summary>
/// Platform user provisioned from Entra ID on first login.
/// Soft-deleted via IsActive (Principle 13: Retract, don't delete).
/// </summary>
public class PlatformUser : EntityBase
{
    public string EntraId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public PlatformRole Role { get; set; } = PlatformRole.Viewer;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// API key for non-interactive (agent/service) authentication.
/// The key itself is stored as a SHA-256 hash — the plaintext is shown
/// only once at creation time.
/// </summary>
public class ApiKey : EntityBase
{
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 hash of the API key. Never store plaintext.</summary>
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>First 8 characters of the key for identification in logs.</summary>
    public string KeyPrefix { get; set; } = string.Empty;

    public Guid IssuedTo { get; set; }
    public PlatformUser IssuedToUser { get; set; } = null!;

    public PlatformRole Role { get; set; } = PlatformRole.Viewer;
    public bool IsActive { get; set; } = true;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
}
