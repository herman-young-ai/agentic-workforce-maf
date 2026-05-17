using System.Reflection;
using AgenticWorkforce.Domain.Entities;
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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Pin every CLR enum property to its native PG enum column type. With
        // EnableUnmappedTypes() on the data source and MapEnum on the EF Core
        // options builder, this is enough to emit the right migration column
        // type and to send native-enum parameters at runtime.
        foreach (var (clrType, pgEnumName) in PgEnumRegistry.All)
        {
            configurationBuilder.Properties(clrType).HaveColumnType(pgEnumName);
        }
    }

    /// <summary>
    /// Generic <c>HasPostgresEnum&lt;TEnum&gt;(ModelBuilder, string?, string?, INpgsqlNameTranslator?)</c>.
    /// Cached so the reflection lookup runs once per process. Used to apply the
    /// CREATE TYPE DDL for every enum in <see cref="PgEnumRegistry"/> without a
    /// hand-maintained second list — there is no non-generic Type overload of
    /// this method, so reflection is the only way to drive it from the registry.
    /// <para>
    /// The filter constrains generic arity (1), parameter count (4), and the
    /// first parameter type (<see cref="ModelBuilder"/>) so we don't silently
    /// pick a future Npgsql overload that happens to share name + arity (e.g. a
    /// <c>ConventionModelBuilder</c> variant).
    /// </para>
    /// </summary>
    private static readonly MethodInfo HasPostgresEnumGeneric =
        typeof(NpgsqlModelBuilderExtensions).GetMethods()
            .Single(m => m.Name == nameof(NpgsqlModelBuilderExtensions.HasPostgresEnum)
                      && m.IsGenericMethodDefinition
                      && m.GetGenericArguments().Length == 1
                      && m.GetParameters() is { Length: 4 } p
                      && p[0].ParameterType == typeof(ModelBuilder));

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // -- PostgreSQL native enum types — driven from PgEnumRegistry --
        foreach (var (clrType, _) in PgEnumRegistry.All)
        {
            HasPostgresEnumGeneric.MakeGenericMethod(clrType)
                .Invoke(null, [modelBuilder, null, null, null]);
        }

        // -- pgvector extension --
        modelBuilder.HasPostgresExtension("vector");

        // xmin row-version tracking is applied via [Timestamp] on EntityBase.RowVersion.
        // Append-only partitioned tables (LlmCall, ProjectEvent) opt out via e.Ignore(...)
        // in their entity configurations.

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
