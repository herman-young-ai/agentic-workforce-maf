using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

internal sealed class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> e)
    {
        e.ToTable("workflow_definitions");
        e.HasOne(w => w.Project).WithMany(p => p.WorkflowDefinitions)
            .HasForeignKey(w => w.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(w => w.ProjectId);
        e.HasIndex(w => new { w.ProjectId, w.Name, w.Version }).IsUnique();
        e.HasIndex(w => new { w.Name, w.Version })
            .IsUnique()
            .HasFilter("project_id IS NULL");
    }
}

internal sealed class WorkflowRunConfiguration : IEntityTypeConfiguration<WorkflowRun>
{
    public void Configure(EntityTypeBuilder<WorkflowRun> e)
    {
        e.ToTable("workflow_runs");
        e.HasOne(r => r.Project).WithMany(p => p.WorkflowRuns)
            .HasForeignKey(r => r.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(r => r.WorkflowDefinition).WithMany()
            .HasForeignKey(r => r.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasOne(r => r.Session).WithMany()
            .HasForeignKey(r => r.SessionId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(r => r.TriggeredByUser).WithMany()
            .HasForeignKey(r => r.TriggeredById)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(r => new { r.ProjectId, r.Status });
        e.HasIndex(r => r.WorkflowDefinitionId);
    }
}

internal sealed class WorkflowScheduleConfiguration : IEntityTypeConfiguration<WorkflowSchedule>
{
    public void Configure(EntityTypeBuilder<WorkflowSchedule> e)
    {
        e.ToTable("workflow_schedules");
        e.HasOne(s => s.Project).WithMany()
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(s => s.WorkflowDefinition).WithMany(w => w.Schedules)
            .HasForeignKey(s => s.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(s => s.ProjectId);
        e.HasIndex(s => s.WorkflowDefinitionId);
        e.HasIndex(s => new { s.Enabled, s.NextRunAt });
    }
}

internal sealed class HumanInputRequestConfiguration : IEntityTypeConfiguration<HumanInputRequest>
{
    public void Configure(EntityTypeBuilder<HumanInputRequest> e)
    {
        e.ToTable("human_input_requests");
        e.HasOne(h => h.Project).WithMany()
            .HasForeignKey(h => h.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(h => h.WorkflowRun).WithMany(r => r.HumanInputRequests)
            .HasForeignKey(h => h.WorkflowRunId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(h => h.Task).WithMany(t => t.HumanInputRequests)
            .HasForeignKey(h => h.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(h => h.Session).WithMany()
            .HasForeignKey(h => h.SessionId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(h => h.Responder).WithMany()
            .HasForeignKey(h => h.ResponderId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(h => new { h.ProjectId, h.Status });
        e.HasIndex(h => new { h.ProjectId, h.Decision });
        e.HasIndex(h => h.WorkflowRunId);
        e.HasIndex(h => h.TaskId);
    }
}
