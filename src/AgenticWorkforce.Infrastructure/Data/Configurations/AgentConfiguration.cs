using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

public class AgentCatalogConfiguration : IEntityTypeConfiguration<AgentCatalog>
{
    public void Configure(EntityTypeBuilder<AgentCatalog> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(128).IsRequired();
        builder.Property(a => a.DisplayName).HasMaxLength(256).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(4000);
        builder.Property(a => a.SystemPrompt).IsRequired();
        builder.Property(a => a.ModelId).HasMaxLength(128).IsRequired();
        builder.Property(a => a.Category).HasMaxLength(64);
        builder.Property(a => a.SemanticVersion).HasMaxLength(32);
        builder.Property(a => a.ExecutionMode).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Tools).HasColumnType("jsonb");
        builder.Property(a => a.McpServers).HasColumnType("jsonb");

        builder.HasIndex(a => new { a.Name, a.SemanticVersion }).IsUnique();
    }
}

public class AgentTemplateConfiguration : IEntityTypeConfiguration<AgentTemplate>
{
    public void Configure(EntityTypeBuilder<AgentTemplate> builder)
    {
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(128).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(4000);
        builder.Property(t => t.SemanticVersion).HasMaxLength(32);

        builder.HasOne(t => t.ParentTemplate).WithMany().HasForeignKey(t => t.ParentTemplateId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(t => t.Name).IsUnique();
    }
}

public class TemplateAgentConfiguration : IEntityTypeConfiguration<TemplateAgent>
{
    public void Configure(EntityTypeBuilder<TemplateAgent> builder)
    {
        builder.HasKey(ta => ta.Id);
        builder.Property(ta => ta.RoleInTeam).HasMaxLength(64);

        builder.HasOne(ta => ta.Template).WithMany(t => t.Agents).HasForeignKey(ta => ta.TemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ta => ta.Agent).WithMany(a => a.TemplateAgents).HasForeignKey(ta => ta.AgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(ta => new { ta.TemplateId, ta.AgentId }).IsUnique();
    }
}

public class ProjectAgentConfiguration : IEntityTypeConfiguration<ProjectAgent>
{
    public void Configure(EntityTypeBuilder<ProjectAgent> builder)
    {
        builder.HasKey(pa => pa.Id);
        builder.Property(pa => pa.PromptOverride).HasMaxLength(8000);
        builder.Property(pa => pa.ToolOverrides).HasColumnType("jsonb");
        builder.Property(pa => pa.RoleInTeam).HasMaxLength(64);

        builder.HasOne(pa => pa.Project).WithMany(p => p.Agents).HasForeignKey(pa => pa.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(pa => pa.Agent).WithMany(a => a.ProjectAgents).HasForeignKey(pa => pa.AgentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(pa => new { pa.ProjectId, pa.AgentId }).IsUnique();
    }
}
