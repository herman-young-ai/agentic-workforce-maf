using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> e)
    {
        e.ToTable("sessions");
        e.HasOne(s => s.Project).WithMany(p => p.Sessions)
            .HasForeignKey(s => s.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(s => s.User).WithMany(u => u.Sessions)
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(s => new { s.ProjectId, s.Status });
        e.HasIndex(s => s.UserId);
    }
}

internal sealed class SessionMessageConfiguration : IEntityTypeConfiguration<SessionMessage>
{
    public void Configure(EntityTypeBuilder<SessionMessage> e)
    {
        e.ToTable("session_messages");
        e.HasOne(m => m.Session).WithMany(s => s.Messages)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(m => m.SessionId);
        e.HasIndex(m => new { m.SessionId, m.CreatedAt });
    }
}

internal sealed class SessionChannelConfiguration : IEntityTypeConfiguration<SessionChannel>
{
    public void Configure(EntityTypeBuilder<SessionChannel> e)
    {
        e.ToTable("session_channels");
        e.HasOne(c => c.Session).WithMany(s => s.Channels)
            .HasForeignKey(c => c.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(c => c.SessionId);
        e.HasIndex(c => new { c.ChannelType, c.ChannelId });
    }
}
