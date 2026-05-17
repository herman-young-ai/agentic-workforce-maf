using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

internal sealed class AgentCatalogConfiguration : IEntityTypeConfiguration<AgentCatalog>
{
    public void Configure(EntityTypeBuilder<AgentCatalog> e)
    {
        e.ToTable("agent_catalogs");
        e.HasIndex(a => a.AgentName).IsUnique();
    }
}

internal sealed class PromptVersionConfiguration : IEntityTypeConfiguration<PromptVersion>
{
    public void Configure(EntityTypeBuilder<PromptVersion> e)
    {
        e.ToTable("prompt_versions");
        e.HasIndex(p => new { p.EntityType, p.EntityId, p.PromptType, p.Version }).IsUnique();
    }
}

/// <summary>
/// LlmCall uses RANGE partitioning by created_at; DDL is created via a raw-SQL
/// migration. EF Core is told to ignore it here so that it generates no
/// CREATE TABLE in the standard migration.
/// </summary>
internal sealed class LlmCallConfiguration : IEntityTypeConfiguration<LlmCall>
{
    public void Configure(EntityTypeBuilder<LlmCall> e)
    {
        e.ToTable("llm_calls", t => t.ExcludeFromMigrations());
        e.Ignore(l => l.RowVersion);
        e.HasKey(l => new { l.Id, l.CreatedAt });
        e.HasOne(l => l.Project).WithMany(p => p.LlmCalls)
            .HasForeignKey(l => l.ProjectId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class ModelPricingConfiguration : IEntityTypeConfiguration<ModelPricing>
{
    public void Configure(EntityTypeBuilder<ModelPricing> e)
    {
        e.ToTable("model_pricing");
        e.HasKey(p => new { p.Model, p.EffectiveFrom });
    }
}
