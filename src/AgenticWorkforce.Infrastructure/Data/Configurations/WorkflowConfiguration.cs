using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AgenticWorkforce.Infrastructure.Data.Configurations;

public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Name).HasMaxLength(256).IsRequired();
        builder.Property(w => w.Description).HasMaxLength(4000);
        builder.Property(w => w.SemanticVersion).HasMaxLength(32);

        builder.HasOne(w => w.Project).WithMany(p => p.Workflows).HasForeignKey(w => w.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class WorkflowNodeConfiguration : IEntityTypeConfiguration<WorkflowNode>
{
    public void Configure(EntityTypeBuilder<WorkflowNode> builder)
    {
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Name).HasMaxLength(256).IsRequired();
        builder.Property(n => n.NodeType).HasConversion<string>().HasMaxLength(32);
        builder.Property(n => n.Config).HasColumnType("jsonb");
        builder.Property(n => n.Position).HasColumnType("jsonb");

        builder.HasOne(n => n.WorkflowDefinition).WithMany(w => w.Nodes).HasForeignKey(n => n.WorkflowDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class WorkflowEdgeConfiguration : IEntityTypeConfiguration<WorkflowEdge>
{
    public void Configure(EntityTypeBuilder<WorkflowEdge> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Condition).HasMaxLength(1000);
        builder.Property(e => e.Label).HasMaxLength(128);

        builder.HasOne(e => e.WorkflowDefinition).WithMany(w => w.Edges).HasForeignKey(e => e.WorkflowDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.SourceNode).WithMany().HasForeignKey(e => e.SourceNodeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.TargetNode).WithMany().HasForeignKey(e => e.TargetNodeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WorkflowExecutionConfiguration : IEntityTypeConfiguration<WorkflowExecution>
{
    public void Configure(EntityTypeBuilder<WorkflowExecution> builder)
    {
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.DurableTaskInstanceId).HasMaxLength(256);
        builder.Property(e => e.Input).HasColumnType("jsonb");
        builder.Property(e => e.Output).HasColumnType("jsonb");

        builder.HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(e => e.WorkflowDefinition).WithMany(w => w.Executions).HasForeignKey(e => e.WorkflowDefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.ProjectId, e.Status });
    }
}

public class WorkflowNodeExecutionConfiguration : IEntityTypeConfiguration<WorkflowNodeExecution>
{
    public void Configure(EntityTypeBuilder<WorkflowNodeExecution> builder)
    {
        builder.HasKey(ne => ne.Id);
        builder.Property(ne => ne.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(ne => ne.Output).HasColumnType("jsonb");

        builder.HasOne(ne => ne.WorkflowExecution).WithMany(e => e.NodeExecutions).HasForeignKey(ne => ne.WorkflowExecutionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(ne => ne.WorkflowNode).WithMany().HasForeignKey(ne => ne.WorkflowNodeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(ne => ne.Task).WithMany().HasForeignKey(ne => ne.TaskId).OnDelete(DeleteBehavior.SetNull);
    }
}
