using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

internal sealed class ProjectLearningConfiguration : IEntityTypeConfiguration<ProjectLearning>
{
    public void Configure(EntityTypeBuilder<ProjectLearning> e)
    {
        e.ToTable("project_learnings", t => t.HasCheckConstraint(
            "ck_project_learnings_confidence", "confidence >= 0 AND confidence <= 1"));
        e.HasOne(l => l.Project).WithMany(p => p.Learnings)
            .HasForeignKey(l => l.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(l => l.Task).WithMany(t => t.Learnings)
            .HasForeignKey(l => l.TaskId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(l => l.SupersededBy).WithMany()
            .HasForeignKey(l => l.SupersededById)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(l => l.Contradicts).WithMany()
            .HasForeignKey(l => l.ContradictsId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(l => new { l.ProjectId, l.Status });
        e.HasIndex(l => l.TaskId);
        e.Property(l => l.Embedding).HasColumnType("vector(1536)");
    }
}

internal sealed class ProjectDecisionConfiguration : IEntityTypeConfiguration<ProjectDecision>
{
    public void Configure(EntityTypeBuilder<ProjectDecision> e)
    {
        e.ToTable("project_decisions");
        e.HasOne(d => d.Project).WithMany(p => p.Decisions)
            .HasForeignKey(d => d.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(d => d.Task).WithMany(t => t.Decisions)
            .HasForeignKey(d => d.TaskId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(d => d.WorkflowRun).WithMany()
            .HasForeignKey(d => d.WorkflowRunId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(d => d.SupersededBy).WithMany()
            .HasForeignKey(d => d.SupersededById)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(d => new { d.ProjectId, d.Status });
        e.HasIndex(d => new { d.ProjectId, d.DecisionRef }).IsUnique();
        e.HasIndex(d => d.WorkflowRunId);
    }
}

internal sealed class MilestoneSummaryConfiguration : IEntityTypeConfiguration<MilestoneSummary>
{
    public void Configure(EntityTypeBuilder<MilestoneSummary> e)
    {
        e.ToTable("milestone_summaries");
        e.HasOne(m => m.Project).WithMany(p => p.MilestoneSummaries)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(m => m.ProjectId);
    }
}

internal sealed class ProjectArtifactConfiguration : IEntityTypeConfiguration<ProjectArtifact>
{
    public void Configure(EntityTypeBuilder<ProjectArtifact> e)
    {
        e.ToTable("project_artifacts");
        e.HasOne(a => a.Project).WithMany(p => p.Artifacts)
            .HasForeignKey(a => a.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(a => a.Task).WithMany(t => t.Artifacts)
            .HasForeignKey(a => a.TaskId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(a => a.ProjectId);
        e.HasIndex(a => a.TaskId);
    }
}
