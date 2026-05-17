using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgenticWorkforce.Infrastructure.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    // -- Project scope --
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectContext> ProjectContexts => Set<ProjectContext>();
    public DbSet<ContextChange> ContextChanges => Set<ContextChange>();
    public DbSet<ContextMilestone> ContextMilestones => Set<ContextMilestone>();
    public DbSet<ProjectIntent> ProjectIntents => Set<ProjectIntent>();
    public DbSet<ProjectAgent> ProjectAgents => Set<ProjectAgent>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();

    // -- Tasks --
    public DbSet<AgenticTask> Tasks => Set<AgenticTask>();
    public DbSet<TaskAttempt> TaskAttempts => Set<TaskAttempt>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();

    // -- Knowledge --
    public DbSet<ProjectLearning> ProjectLearnings => Set<ProjectLearning>();
    public DbSet<ProjectDecision> ProjectDecisions => Set<ProjectDecision>();
    public DbSet<MilestoneSummary> MilestoneSummaries => Set<MilestoneSummary>();

    // -- Artifacts & documents --
    public DbSet<ProjectArtifact> ProjectArtifacts => Set<ProjectArtifact>();
    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();
    public DbSet<DocumentChunk> DocumentChunks => Set<DocumentChunk>();

    // -- Sessions --
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<SessionMessage> SessionMessages => Set<SessionMessage>();
    public DbSet<SessionChannel> SessionChannels => Set<SessionChannel>();

    // -- Workflows --
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();
    public DbSet<WorkflowSchedule> WorkflowSchedules => Set<WorkflowSchedule>();
    public DbSet<HumanInputRequest> HumanInputRequests => Set<HumanInputRequest>();

    // -- Events --
    public DbSet<ProjectEvent> ProjectEvents => Set<ProjectEvent>();

    // -- Platform --
    public DbSet<User> Users => Set<User>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AgentCatalog> AgentCatalogs => Set<AgentCatalog>();
    public DbSet<PromptVersion> PromptVersions => Set<PromptVersion>();
    public DbSet<LlmCall> LlmCalls => Set<LlmCall>();
    public DbSet<ModelPricing> ModelPricings => Set<ModelPricing>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // -- PostgreSQL enums --
        modelBuilder.HasPostgresEnum<ProjectStatus>();
        modelBuilder.HasPostgresEnum<ProjectTier>();
        modelBuilder.HasPostgresEnum<ProjectRole>();
        modelBuilder.HasPostgresEnum<SystemRole>();
        modelBuilder.HasPostgresEnum<ChangeType>();
        modelBuilder.HasPostgresEnum<IntentSource>();
        modelBuilder.HasPostgresEnum<AgentRole>();
        modelBuilder.HasPostgresEnum<TaskType>();
        modelBuilder.HasPostgresEnum<TaskStatus>();
        modelBuilder.HasPostgresEnum<TaskSource>();
        modelBuilder.HasPostgresEnum<AttemptStatus>();
        modelBuilder.HasPostgresEnum<FailureTier>();
        modelBuilder.HasPostgresEnum<LearningKind>();
        modelBuilder.HasPostgresEnum<LearningStatus>();
        modelBuilder.HasPostgresEnum<DecisionStatus>();
        modelBuilder.HasPostgresEnum<ContentFormat>();
        modelBuilder.HasPostgresEnum<ArtifactType>();
        modelBuilder.HasPostgresEnum<DocumentType>();
        modelBuilder.HasPostgresEnum<ExtractionStatus>();
        modelBuilder.HasPostgresEnum<SessionStatus>();
        modelBuilder.HasPostgresEnum<MessageRole>();
        modelBuilder.HasPostgresEnum<WorkflowRunStatus>();
        modelBuilder.HasPostgresEnum<HumanInputRequestStatus>();
        modelBuilder.HasPostgresEnum<HumanDecisionType>();
        modelBuilder.HasPostgresEnum<EventSeverity>();
        modelBuilder.HasPostgresEnum<AgentVisibility>();

        // -- pgvector extension --
        modelBuilder.HasPostgresExtension("vector");

        // xmin row-version tracking is applied via [Timestamp] on EntityBase.RowVersion.
        // Append-only partitioned tables (LlmCall, ProjectEvent) opt out below via e.Ignore(...).

        // ── Project ──────────────────────────────────────────
        modelBuilder.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.HasIndex(p => p.Name).IsUnique();
            e.HasIndex(p => p.Status);
        });

        // ── ProjectContext (1:1) ─────────────────────────────
        modelBuilder.Entity<ProjectContext>(e =>
        {
            e.ToTable("project_contexts");
            e.HasOne(c => c.Project).WithOne(p => p.Context)
                .HasForeignKey<ProjectContext>(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.ProjectId).IsUnique();
        });

        modelBuilder.Entity<ContextChange>(e =>
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
        });

        modelBuilder.Entity<ContextMilestone>(e =>
        {
            e.ToTable("context_milestones");
            e.HasOne(m => m.Project).WithMany(p => p.Milestones)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.ProjectId);
        });

        modelBuilder.Entity<ProjectIntent>(e =>
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
        });

        modelBuilder.Entity<ProjectAgent>(e =>
        {
            e.ToTable("project_agents");
            e.HasOne(a => a.Project).WithMany(p => p.Agents)
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.AgentCatalog).WithMany(c => c.ProjectAgents)
                .HasForeignKey(a => a.AgentCatalogId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(a => new { a.ProjectId, a.AgentCatalogId }).IsUnique();
        });

        modelBuilder.Entity<ProjectMember>(e =>
        {
            e.ToTable("project_members");
            e.HasOne(m => m.Project).WithMany(p => p.Members)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(m => m.User).WithMany(u => u.Memberships)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
        });

        // ── Tasks ────────────────────────────────────────────
        modelBuilder.Entity<AgenticTask>(e =>
        {
            e.ToTable("tasks");
            e.HasOne(t => t.Project).WithMany(p => p.Tasks)
                .HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(t => t.ParentTask).WithMany(t => t.ChildTasks)
                .HasForeignKey(t => t.ParentTaskId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.WorkflowRun).WithMany(r => r.Tasks)
                .HasForeignKey(t => t.WorkflowRunId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.AssignedTo).WithMany()
                .HasForeignKey(t => t.AssignedToId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.Session).WithMany()
                .HasForeignKey(t => t.SessionId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(t => t.CreatedBy).WithMany()
                .HasForeignKey(t => t.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(t => new { t.ProjectId, t.Status });
            e.HasIndex(t => new { t.ProjectId, t.CreatedAt });
            e.HasIndex(t => new { t.ProjectId, t.StartedAt });
            e.HasIndex(t => t.WorkflowRunId);
            e.HasIndex(t => t.ParentTaskId);
            e.HasIndex(t => t.AssignedToId);
            e.HasIndex(t => t.SessionId);
        });

        modelBuilder.Entity<TaskAttempt>(e =>
        {
            e.ToTable("task_attempts");
            e.HasOne(a => a.Project).WithMany()
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Task).WithMany(t => t.Attempts)
                .HasForeignKey(a => a.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => new { a.TaskId, a.AttemptNumber }).IsUnique();
            e.HasIndex(a => a.ProjectId);
        });

        modelBuilder.Entity<TaskDependency>(e =>
        {
            e.ToTable("task_dependencies");
            e.HasKey(d => new { d.TaskId, d.DependsOnTaskId });
            e.HasOne(d => d.Task).WithMany(t => t.Dependencies)
                .HasForeignKey(d => d.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.DependsOnTask).WithMany(t => t.Dependents)
                .HasForeignKey(d => d.DependsOnTaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Knowledge ────────────────────────────────────────
        modelBuilder.Entity<ProjectLearning>(e =>
        {
            e.ToTable("project_learnings");
            e.HasOne(l => l.Project).WithMany(p => p.Learnings)
                .HasForeignKey(l => l.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(l => l.Task).WithMany(t => t.Learnings)
                .HasForeignKey(l => l.TaskId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(l => l.SupersededBy).WithMany()
                .HasForeignKey(l => l.SupersededById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(l => l.Contradicts).WithMany()
                .HasForeignKey(l => l.ContradictsId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(l => new { l.ProjectId, l.Status });
            e.HasIndex(l => l.TaskId);
            e.ToTable(t => t.HasCheckConstraint(
                "ck_project_learnings_confidence", "confidence >= 0 AND confidence <= 1"));
            e.Property(l => l.Embedding).HasColumnType("vector(1536)");
        });

        modelBuilder.Entity<ProjectDecision>(e =>
        {
            e.ToTable("project_decisions");
            e.HasOne(d => d.Project).WithMany(p => p.Decisions)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.Task).WithMany(t => t.Decisions)
                .HasForeignKey(d => d.TaskId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.WorkflowRun).WithMany()
                .HasForeignKey(d => d.WorkflowRunId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(d => d.SupersededBy).WithMany()
                .HasForeignKey(d => d.SupersededById)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(d => new { d.ProjectId, d.Status });
            e.HasIndex(d => new { d.ProjectId, d.DecisionRef }).IsUnique();
            e.HasIndex(d => d.WorkflowRunId);
        });

        modelBuilder.Entity<MilestoneSummary>(e =>
        {
            e.ToTable("milestone_summaries");
            e.HasOne(m => m.Project).WithMany(p => p.MilestoneSummaries)
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.ProjectId);
        });

        // ── Artifacts & documents ────────────────────────────
        modelBuilder.Entity<ProjectArtifact>(e =>
        {
            e.ToTable("project_artifacts");
            e.HasOne(a => a.Project).WithMany(p => p.Artifacts)
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(a => a.Task).WithMany(t => t.Artifacts)
                .HasForeignKey(a => a.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(a => a.ProjectId);
            e.HasIndex(a => a.TaskId);
        });

        modelBuilder.Entity<ProjectDocument>(e =>
        {
            e.ToTable("project_documents");
            e.HasOne(d => d.Project).WithMany(p => p.Documents)
                .HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(d => d.UploadedBy).WithMany()
                .HasForeignKey(d => d.UploadedById)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(d => d.ProjectId);
        });

        modelBuilder.Entity<DocumentChunk>(e =>
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
        });

        // ── Sessions ─────────────────────────────────────────
        modelBuilder.Entity<Session>(e =>
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
        });

        modelBuilder.Entity<SessionMessage>(e =>
        {
            e.ToTable("session_messages");
            e.HasOne(m => m.Session).WithMany(s => s.Messages)
                .HasForeignKey(m => m.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.SessionId);
            e.HasIndex(m => new { m.SessionId, m.CreatedAt });
        });

        modelBuilder.Entity<SessionChannel>(e =>
        {
            e.ToTable("session_channels");
            e.HasOne(c => c.Session).WithMany(s => s.Channels)
                .HasForeignKey(c => c.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.SessionId);
            e.HasIndex(c => new { c.ChannelType, c.ChannelId });
        });

        // ── Workflows ────────────────────────────────────────
        modelBuilder.Entity<WorkflowDefinition>(e =>
        {
            e.ToTable("workflow_definitions");
            e.HasOne(w => w.Project).WithMany(p => p.WorkflowDefinitions)
                .HasForeignKey(w => w.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(w => w.ProjectId);
            e.HasIndex(w => new { w.ProjectId, w.Name, w.Version }).IsUnique();
            e.HasIndex(w => new { w.Name, w.Version })
                .IsUnique()
                .HasFilter("project_id IS NULL");
        });

        modelBuilder.Entity<WorkflowRun>(e =>
        {
            e.ToTable("workflow_runs");
            e.HasOne(r => r.Project).WithMany(p => p.WorkflowRuns)
                .HasForeignKey(r => r.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(r => r.WorkflowDefinition).WithMany()
                .HasForeignKey(r => r.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(r => r.Session).WithMany()
                .HasForeignKey(r => r.SessionId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(r => new { r.ProjectId, r.Status });
            e.HasIndex(r => r.WorkflowDefinitionId);
        });

        modelBuilder.Entity<WorkflowSchedule>(e =>
        {
            e.ToTable("workflow_schedules");
            e.HasOne(s => s.Project).WithMany()
                .HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(s => s.WorkflowDefinition).WithMany(w => w.Schedules)
                .HasForeignKey(s => s.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(s => s.ProjectId);
            e.HasIndex(s => s.WorkflowDefinitionId);
            e.HasIndex(s => new { s.Enabled, s.NextRunAt });
        });

        modelBuilder.Entity<HumanInputRequest>(e =>
        {
            e.ToTable("human_input_requests");
            e.HasOne(h => h.Project).WithMany()
                .HasForeignKey(h => h.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.WorkflowRun).WithMany(r => r.HumanInputRequests)
                .HasForeignKey(h => h.WorkflowRunId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.Task).WithMany(t => t.HumanInputRequests)
                .HasForeignKey(h => h.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(h => h.Session).WithMany()
                .HasForeignKey(h => h.SessionId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(h => h.Responder).WithMany()
                .HasForeignKey(h => h.ResponderId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(h => new { h.ProjectId, h.Status });
            e.HasIndex(h => new { h.ProjectId, h.Decision });
            e.HasIndex(h => h.WorkflowRunId);
            e.HasIndex(h => h.TaskId);
        });

        // ── ProjectEvent (partitioned — DDL via raw SQL migration) ──
        modelBuilder.Entity<ProjectEvent>(e =>
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
        });

        // ── User ─────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<ApiKey>(e =>
        {
            e.ToTable("api_keys");
            e.HasOne(k => k.User).WithMany(u => u.ApiKeys)
                .HasForeignKey(k => k.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(k => new { k.UserId, k.Name }).IsUnique();
            e.HasIndex(k => k.KeyPrefix);
        });

        modelBuilder.Entity<AgentCatalog>(e =>
        {
            e.ToTable("agent_catalogs");
            e.HasIndex(a => a.AgentName).IsUnique();
        });

        modelBuilder.Entity<PromptVersion>(e =>
        {
            e.ToTable("prompt_versions");
            e.HasIndex(p => new { p.EntityType, p.EntityId, p.PromptType, p.Version }).IsUnique();
        });

        // ── LlmCall (partitioned — DDL via raw SQL migration) ──
        modelBuilder.Entity<LlmCall>(e =>
        {
            e.ToTable("llm_calls", t => t.ExcludeFromMigrations());
            e.Ignore(l => l.RowVersion);
            e.HasKey(l => new { l.Id, l.CreatedAt });
            e.HasOne(l => l.Project).WithMany(p => p.LlmCalls)
                .HasForeignKey(l => l.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ModelPricing>(e =>
        {
            e.ToTable("model_pricing");
            e.HasKey(p => new { p.Model, p.EffectiveFrom });
        });
    }
}
