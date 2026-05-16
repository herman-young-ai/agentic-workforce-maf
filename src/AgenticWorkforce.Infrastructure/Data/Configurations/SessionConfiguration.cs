using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Title).HasMaxLength(512);
        builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(s => s.Project).WithMany(p => p.Sessions).HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(s => new { s.ProjectId, s.IsActive });
    }
}

public class SessionMessageConfiguration : IEntityTypeConfiguration<SessionMessage>
{
    public void Configure(EntityTypeBuilder<SessionMessage> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Role).HasConversion<string>().HasMaxLength(32);
        builder.Property(m => m.Content).IsRequired();
        builder.Property(m => m.AgentName).HasMaxLength(128);
        builder.Property(m => m.ToolCalls).HasColumnType("jsonb");

        builder.HasOne(m => m.Session).WithMany(s => s.Messages).HasForeignKey(m => m.SessionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => m.SessionId);
    }
}
