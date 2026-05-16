using AgenticWorkforce.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // -- Core --
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<AgenticTask> Tasks => Set<AgenticTask>();
    public DbSet<TaskAttempt> TaskAttempts => Set<TaskAttempt>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();

    // -- Sessions --
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionMessage> SessionMessages => Set<SessionMessage>();

    // -- Agents --
    public DbSet<AgentCatalog> AgentCatalog => Set<AgentCatalog>();
    public DbSet<AgentTemplate> AgentTemplates => Set<AgentTemplate>();
    public DbSet<TemplateAgent> TemplateAgents => Set<TemplateAgent>();
    public DbSet<ProjectAgent> ProjectAgents => Set<ProjectAgent>();

    // -- Workflows --
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowNode> WorkflowNodes => Set<WorkflowNode>();
    public DbSet<WorkflowEdge> WorkflowEdges => Set<WorkflowEdge>();
    public DbSet<WorkflowExecution> WorkflowExecutions => Set<WorkflowExecution>();
    public DbSet<WorkflowNodeExecution> WorkflowNodeExecutions => Set<WorkflowNodeExecution>();

    // -- Knowledge --
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();
    public DbSet<ProjectLearning> ProjectLearnings => Set<ProjectLearning>();
    public DbSet<Decision> Decisions => Set<Decision>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();

    // -- Audit & Cost --
    public DbSet<ProjectEvent> ProjectEvents => Set<ProjectEvent>();
    public DbSet<LlmCall> LlmCalls => Set<LlmCall>();
    public DbSet<CostBudget> CostBudgets => Set<CostBudget>();

    // -- Identity --
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // pgvector extension
        modelBuilder.HasPostgresExtension("vector");

        // Apply all IEntityTypeConfiguration<T> from this assembly
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
