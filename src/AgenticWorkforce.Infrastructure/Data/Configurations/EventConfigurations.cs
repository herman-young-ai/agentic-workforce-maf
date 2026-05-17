using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

/// <summary>
/// ProjectEvent uses RANGE partitioning by created_at; DDL is created via a
/// raw-SQL migration. EF Core is told to ignore it here so that it generates no
/// CREATE TABLE in the standard migration.
/// </summary>
internal sealed class ProjectEventConfiguration : IEntityTypeConfiguration<ProjectEvent>
{
    public void Configure(EntityTypeBuilder<ProjectEvent> e)
    {
        e.ToTable("project_events", t => t.ExcludeFromMigrations());
        e.Ignore(ev => ev.RowVersion);
        e.HasKey(ev => new { ev.Id, ev.CreatedAt });
        e.HasOne(ev => ev.Project).WithMany(p => p.Events)
            .HasForeignKey(ev => ev.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(ev => ev.Task).WithMany(t => t.Events)
            .HasForeignKey(ev => ev.TaskId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasOne(ev => ev.Session).WithMany()
            .HasForeignKey(ev => ev.SessionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
