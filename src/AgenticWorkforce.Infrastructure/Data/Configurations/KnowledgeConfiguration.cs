using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

public class ProjectDocumentConfiguration : IEntityTypeConfiguration<ProjectDocument>
{
    public void Configure(EntityTypeBuilder<ProjectDocument> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.FileName).HasMaxLength(512).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(d => d.BlobUri).HasMaxLength(2048).IsRequired();
        builder.Property(d => d.ContentHash).HasMaxLength(64).IsRequired();

        builder.HasOne(d => d.Project).WithMany(p => p.Documents).HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => new { d.ProjectId, d.ContentHash }).IsUnique();
    }
}

public class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Content).IsRequired();
        builder.Property(c => c.Embedding).HasColumnType("vector(1536)");

        builder.HasOne(c => c.Document).WithMany(d => d.Chunks).HasForeignKey(c => c.DocumentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.DocumentId, c.ChunkIndex }).IsUnique();
    }
}

public class ProjectLearningConfiguration : IEntityTypeConfiguration<ProjectLearning>
{
    public void Configure(EntityTypeBuilder<ProjectLearning> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(l => l.Content).IsRequired();
        builder.Property(l => l.Source).HasMaxLength(512);
        builder.Property(l => l.ExtractedByAgent).HasMaxLength(128);
        builder.Property(l => l.RetractedReason).HasMaxLength(1000);
        builder.Property(l => l.Embedding).HasColumnType("vector(1536)");

        builder.HasOne(l => l.Project).WithMany(p => p.Learnings).HasForeignKey(l => l.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(l => l.Task).WithMany().HasForeignKey(l => l.TaskId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(l => new { l.ProjectId, l.Kind, l.Status });
    }
}

public class DecisionConfiguration : IEntityTypeConfiguration<Decision>
{
    public void Configure(EntityTypeBuilder<Decision> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Rationale).HasMaxLength(4000);

        builder.HasOne(d => d.Task).WithMany(t => t.Decisions).HasForeignKey(d => d.TaskId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(d => d.DecidedByUser).WithMany().HasForeignKey(d => d.DecidedBy).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ArtifactConfiguration : IEntityTypeConfiguration<Artifact>
{
    public void Configure(EntityTypeBuilder<Artifact> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(512).IsRequired();
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.ContentType).HasMaxLength(128).IsRequired();
        builder.Property(a => a.BlobUri).HasMaxLength(2048).IsRequired();
        builder.Property(a => a.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Metadata).HasColumnType("jsonb");

        builder.HasOne(a => a.Task).WithMany(t => t.Artifacts).HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
    }
}
