using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

public class TaskConfiguration : IEntityTypeConfiguration<AgenticTask>
{
    public void Configure(EntityTypeBuilder<AgenticTask> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Title).HasMaxLength(512).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(8000);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.Priority).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.Input).HasColumnType("jsonb");
        builder.Property(t => t.Output).HasColumnType("jsonb");

        builder.HasOne(t => t.Project).WithMany(p => p.Tasks).HasForeignKey(t => t.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(t => t.AssignedAgent).WithMany().HasForeignKey(t => t.AssignedAgentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne(t => t.ParentTask).WithMany(t => t.SubTasks).HasForeignKey(t => t.ParentTaskId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.ProjectId, t.Status });
    }
}

public class TaskAttemptConfiguration : IEntityTypeConfiguration<TaskAttempt>
{
    public void Configure(EntityTypeBuilder<TaskAttempt> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Input).HasColumnType("jsonb");
        builder.Property(a => a.Output).HasColumnType("jsonb");
        builder.Property(a => a.CostUsd).HasPrecision(12, 6);

        builder.HasOne(a => a.Task).WithMany(t => t.Attempts).HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.TaskId, a.AttemptNumber }).IsUnique();
    }
}

public class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.HasKey(d => new { d.TaskId, d.DependsOnTaskId });

        builder.HasOne(d => d.Task).WithMany(t => t.Dependencies).HasForeignKey(d => d.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(d => d.DependsOnTask).WithMany(t => t.Dependents).HasForeignKey(d => d.DependsOnTaskId).OnDelete(DeleteBehavior.Cascade);
    }
}
