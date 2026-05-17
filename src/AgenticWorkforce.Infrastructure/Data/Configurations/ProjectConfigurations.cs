using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> e)
    {
        e.ToTable("projects");
        e.HasIndex(p => p.Name).IsUnique();
        e.HasIndex(p => p.Status);
    }
}

internal sealed class ProjectContextConfiguration : IEntityTypeConfiguration<ProjectContext>
{
    public void Configure(EntityTypeBuilder<ProjectContext> e)
    {
        e.ToTable("project_contexts");
        e.HasOne(c => c.Project).WithOne(p => p.Context)
            .HasForeignKey<ProjectContext>(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(c => c.ProjectId).IsUnique();
    }
}

internal sealed class ContextChangeConfiguration : IEntityTypeConfiguration<ContextChange>
{
    public void Configure(EntityTypeBuilder<ContextChange> e)
    {
        e.ToTable("context_changes");
        e.HasOne(c => c.Project).WithMany()
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(c => c.Context).WithMany(pc => pc.Changes)
            .HasForeignKey(c => c.ContextId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(c => c.ContextId);
        e.HasIndex(c => c.ProjectId);
    }
}

internal sealed class ContextMilestoneConfiguration : IEntityTypeConfiguration<ContextMilestone>
{
    public void Configure(EntityTypeBuilder<ContextMilestone> e)
    {
        e.ToTable("context_milestones");
        e.HasOne(m => m.Project).WithMany(p => p.Milestones)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(m => m.ProjectId);
    }
}

internal sealed class ProjectIntentConfiguration : IEntityTypeConfiguration<ProjectIntent>
{
    public void Configure(EntityTypeBuilder<ProjectIntent> e)
    {
        e.ToTable("project_intents");
        e.HasOne(i => i.Project).WithMany(p => p.Intents)
            .HasForeignKey(i => i.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(i => i.RevisedFrom).WithMany()
            .HasForeignKey(i => i.RevisedFromId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(i => i.Session).WithMany()
            .HasForeignKey(i => i.SessionId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(i => i.ProjectId);
    }
}

internal sealed class ProjectMemberConfiguration : IEntityTypeConfiguration<ProjectMember>
{
    public void Configure(EntityTypeBuilder<ProjectMember> e)
    {
        e.ToTable("project_members");
        e.HasOne(m => m.Project).WithMany(p => p.Members)
            .HasForeignKey(m => m.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(m => m.User).WithMany(u => u.Memberships)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
    }
}

internal sealed class ProjectAgentConfiguration : IEntityTypeConfiguration<ProjectAgent>
{
    public void Configure(EntityTypeBuilder<ProjectAgent> e)
    {
        e.ToTable("project_agents");
        e.HasOne(a => a.Project).WithMany(p => p.Agents)
            .HasForeignKey(a => a.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(a => a.AgentCatalog).WithMany(c => c.ProjectAgents)
            .HasForeignKey(a => a.AgentCatalogId)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(a => new { a.ProjectId, a.AgentCatalogId }).IsUnique();
    }
}
