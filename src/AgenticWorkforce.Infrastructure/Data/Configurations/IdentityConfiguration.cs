using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

public class PlatformUserConfiguration : IEntityTypeConfiguration<PlatformUser>
{
    public void Configure(EntityTypeBuilder<PlatformUser> builder)
    {
        builder.HasKey(u => u.Id);
        builder.Property(u => u.EntraId).HasMaxLength(128).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(32);

        builder.HasQueryFilter(u => u.IsActive);
        builder.HasIndex(u => u.EntraId).IsUnique();
        builder.HasIndex(u => u.Email).IsUnique();
    }
}

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Name).HasMaxLength(128).IsRequired();
        builder.Property(k => k.KeyHash).HasMaxLength(64).IsRequired();
        builder.Property(k => k.KeyPrefix).HasMaxLength(16).IsRequired();
        builder.Property(k => k.Role).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(k => k.IssuedToUser).WithMany().HasForeignKey(k => k.IssuedTo).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(k => k.KeyHash).IsUnique();
        builder.HasIndex(k => k.KeyPrefix);
    }
}
