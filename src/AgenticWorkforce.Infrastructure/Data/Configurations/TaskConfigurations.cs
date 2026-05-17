using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

internal sealed class AgenticTaskConfiguration : IEntityTypeConfiguration<AgenticTask>
{
    public void Configure(EntityTypeBuilder<AgenticTask> e)
    {
        e.ToTable("tasks");
        e.HasOne(t => t.Project).WithMany(p => p.Tasks)
            .HasForeignKey(t => t.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(t => t.ParentTask).WithMany(t => t.ChildTasks)
            .HasForeignKey(t => t.ParentTaskId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(t => t.WorkflowRun).WithMany(r => r.Tasks)
            .HasForeignKey(t => t.WorkflowRunId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(t => t.AssignedTo).WithMany()
            .HasForeignKey(t => t.AssignedToId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(t => t.Session).WithMany()
            .HasForeignKey(t => t.SessionId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(t => t.CreatedBy).WithMany()
            .HasForeignKey(t => t.CreatedById)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(t => new { t.ProjectId, t.Status });
        e.HasIndex(t => new { t.ProjectId, t.CreatedAt });
        e.HasIndex(t => new { t.ProjectId, t.StartedAt });
        e.HasIndex(t => t.WorkflowRunId);
        e.HasIndex(t => t.ParentTaskId);
        e.HasIndex(t => t.AssignedToId);
        e.HasIndex(t => t.SessionId);
    }
}

internal sealed class TaskAttemptConfiguration : IEntityTypeConfiguration<TaskAttempt>
{
    public void Configure(EntityTypeBuilder<TaskAttempt> e)
    {
        e.ToTable("task_attempts");
        e.HasOne(a => a.Project).WithMany()
            .HasForeignKey(a => a.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(a => a.Task).WithMany(t => t.Attempts)
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(a => new { a.TaskId, a.AttemptNumber }).IsUnique();
        e.HasIndex(a => a.ProjectId);
    }
}

internal sealed class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> e)
    {
        e.ToTable("task_dependencies");
        e.HasKey(d => new { d.TaskId, d.DependsOnTaskId });
        e.HasOne(d => d.Task).WithMany(t => t.Dependencies)
            .HasForeignKey(d => d.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(d => d.DependsOnTask).WithMany(t => t.Dependents)
            .HasForeignKey(d => d.DependsOnTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
