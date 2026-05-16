using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).HasMaxLength(256).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(4000);
        builder.Property(p => p.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Priority).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.SecurityClassification).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Settings).HasColumnType("jsonb");
        builder.Property(p => p.Metadata).HasColumnType("jsonb");

        builder.HasOne(p => p.Owner).WithMany().HasForeignKey(p => p.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.TenantId);
        builder.HasIndex(p => p.Status);
    }
}

public class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> builder)
    {
        builder.HasKey(pm => pm.Id);
        builder.Property(pm => pm.Role).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(pm => pm.Project).WithMany(p => p.Members).HasForeignKey(pm => pm.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(pm => pm.User).WithMany().HasForeignKey(pm => pm.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(pm => new { pm.ProjectId, pm.UserId }).IsUnique();
    }
}
