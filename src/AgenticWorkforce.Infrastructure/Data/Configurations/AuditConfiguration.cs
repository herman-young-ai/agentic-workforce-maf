using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

public class ProjectEventConfiguration : IEntityTypeConfiguration<ProjectEvent>
{
    public void Configure(EntityTypeBuilder<ProjectEvent> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.EventType).HasMaxLength(128).IsRequired();
        builder.Property(e => e.Severity).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.Message).IsRequired();
        builder.Property(e => e.Data).HasColumnType("jsonb");
        builder.Property(e => e.AgentName).HasMaxLength(128);

        builder.HasOne(e => e.Project).WithMany(p => p.Events).HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.Task).WithMany().HasForeignKey(e => e.TaskId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(e => new { e.ProjectId, e.CreatedAt });
    }
}

public class LlmCallConfiguration : IEntityTypeConfiguration<LlmCall>
{
    public void Configure(EntityTypeBuilder<LlmCall> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.AgentName).HasMaxLength(128).IsRequired();
        builder.Property(c => c.ModelId).HasMaxLength(128).IsRequired();
        builder.Property(c => c.Provider).HasMaxLength(64).IsRequired();
        builder.Property(c => c.CostUsd).HasPrecision(12, 6);
        builder.Property(c => c.Error).HasMaxLength(4000);

        builder.HasOne(c => c.Project).WithMany().HasForeignKey(c => c.ProjectId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(c => new { c.ProjectId, c.CreatedAt });
        builder.HasIndex(c => c.AgentName);
    }
}

public class CostBudgetConfiguration : IEntityTypeConfiguration<CostBudget>
{
    public void Configure(EntityTypeBuilder<CostBudget> builder)
    {
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Scope).HasConversion<string>().HasMaxLength(32);
        builder.Property(b => b.ScopeId).HasMaxLength(256);
        builder.Property(b => b.LimitUsd).HasPrecision(12, 2);
        builder.Property(b => b.UsedUsd).HasPrecision(12, 2);
        builder.Property(b => b.AlertThreshold).HasPrecision(5, 4);

        builder.HasOne(b => b.Project).WithMany(p => p.Budgets).HasForeignKey(b => b.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(b => new { b.ProjectId, b.Scope, b.ScopeId }).IsUnique();
    }
}
