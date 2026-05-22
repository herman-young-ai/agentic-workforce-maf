using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Data;
using AgenticWorkforce.Infrastructure.Repositories;
using AgenticWorkforce.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AgenticWorkforce.Infrastructure;

public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Wires the EF Core data layer, repositories, and service stubs. Reads
    /// settings from <see cref="IConfiguration"/> so integration tests can
    /// override the connection string with an in-memory configuration source
    /// instead of stripping and re-binding registrations in DI.
    /// <para>Configuration keys used:</para>
    /// <list type="bullet">
    ///   <item><c>ConnectionStrings:agenticworkforce</c> — required.</item>
    ///   <item><c>DocumentStore:BasePath</c> — optional; defaults to
    ///     <c>{TempPath}/agenticworkforce-docs</c>.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("agenticworkforce")
            ?? throw new InvalidOperationException(
                "Connection string 'agenticworkforce' is required.");

        var dataSource = DataSourceFactory.Create(connectionString);
        services.AddSingleton(dataSource);

        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<AppDbContext>((sp, opts) =>
            opts.UseAgenticWorkforce(sp.GetRequiredService<NpgsqlDataSource>(), sp.GetRequiredService<AuditInterceptor>()));

        // Repositories — every Api handler's data access flows through these
        // (rule DL-001, Principle 4 Wrap the Core). AppDbContext stays internal
        // to Infrastructure.
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IProjectMemberRepository, ProjectMemberRepository>();
        services.AddScoped<IProjectAgentRepository, ProjectAgentRepository>();
        services.AddScoped<IAgentCatalogRepository, AgentCatalogRepository>();
        services.AddScoped<IPromptVersionRepository, PromptVersionRepository>();

        // Phase 4 repositories.
        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowRunRepository, WorkflowRunRepository>();
        services.AddScoped<IWorkflowScheduleRepository, WorkflowScheduleRepository>();
        services.AddScoped<IHumanInputRepository, HumanInputRepository>();
        services.AddScoped<IProjectContextRepository, ProjectContextRepository>();
        services.AddScoped<ILearningRepository, LearningRepository>();
        services.AddScoped<IMilestoneRepository, MilestoneRepository>();
        services.AddScoped<IDecisionRepository, DecisionRepository>();
        services.AddScoped<IIntentRepository, IntentRepository>();
        services.AddScoped<IArtifactRepository, ArtifactRepository>();
        services.AddScoped<IDocumentRepository, DocumentRepository>();
        services.AddScoped<IEventRepository, EventRepository>();
        services.AddScoped<ICatalogQueryRepository, CatalogQueryRepository>();

        // Execution dispatch — in-memory stub until Phase 5/8 wires Redis Streams.
        // Singleton because the stub holds the in-flight ID -> status dictionary.
        services.AddSingleton<IExecutionRepository, InMemoryExecutionRepository>();

        // Phase 4 services.
        services.AddScoped<IProjectContextService, ProjectContextService>();
        services.AddScoped<ICostQueryService, CostQueryService>();
        services.AddSingleton<IWorkflowValidator, WorkflowValidator>();
        services.Configure<CostQueryOptions>(opts =>
        {
            if (int.TryParse(configuration["CostQuery:MaxRangeDays"], out var days) && days > 0)
                opts.MaxRangeDays = days;
        });

        // Service stubs — replaced in Phase 6+ (embedding provider) and Phase 11 (blob storage)
        services.AddScoped<IEmbeddingService, StubEmbeddingService>();

        var docRoot = configuration["DocumentStore:BasePath"]
            ?? Path.Combine(Path.GetTempPath(), "agenticworkforce-docs");
        Directory.CreateDirectory(docRoot);
        services.AddScoped<IDocumentStore>(_ => new LocalFileDocumentStore(docRoot));

        return services;
    }
}
