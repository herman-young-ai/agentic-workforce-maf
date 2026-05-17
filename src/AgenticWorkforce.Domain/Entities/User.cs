using System.ComponentModel.DataAnnotations.Schema;
using AgenticWorkforce.Domain.Enums;

namespace AgenticWorkforce.Domain.Entities;

public class User : EntityBase
{
    public string Email { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? HashedPassword { get; set; }
    public SystemRole SystemRole { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsServiceAccount { get; set; }
    public DateTime? LastLoginAt { get; set; }

    public ICollection<ApiKey> ApiKeys { get; set; } = [];
    public ICollection<ProjectMember> Memberships { get; set; } = [];
    public ICollection<Session> Sessions { get; set; } = [];
}

public class ApiKey : EntityBase
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string KeyPrefix { get; set; } = null!;
    public string HashedKey { get; set; } = null!;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }

    [Column(TypeName = "jsonb")]
    public string? Scopes { get; set; }
}
