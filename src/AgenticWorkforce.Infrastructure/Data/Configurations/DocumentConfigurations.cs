using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

internal sealed class ProjectDocumentConfiguration : IEntityTypeConfiguration<ProjectDocument>
{
    public void Configure(EntityTypeBuilder<ProjectDocument> e)
    {
        e.ToTable("project_documents");
        e.HasOne(d => d.Project).WithMany(p => p.Documents)
            .HasForeignKey(d => d.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(d => d.UploadedBy).WithMany()
            .HasForeignKey(d => d.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);
        e.HasIndex(d => d.ProjectId);
    }
}

internal sealed class DocumentChunkConfiguration : IEntityTypeConfiguration<DocumentChunk>
{
    public void Configure(EntityTypeBuilder<DocumentChunk> e)
    {
        e.ToTable("document_chunks");
        e.HasOne(c => c.Project).WithMany()
            .HasForeignKey(c => c.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasOne(c => c.Document).WithMany(d => d.Chunks)
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(c => c.DocumentId);
        e.HasIndex(c => c.ProjectId);
        e.Property(c => c.Embedding).HasColumnType("vector(1536)");
    }
}
