using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Domain.Services;
using AgenticWorkforce.Infrastructure.Data;
using AgenticWorkforce.Infrastructure.Events;
using AgenticWorkforce.Infrastructure.Repositories;
using AgenticWorkforce.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StackExchange.Redis;

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
        // The NpgsqlDataSource is resolved through the DI container so the
        // connection string is read AFTER WebApplicationFactory's
        // ConfigureAppConfiguration overrides have applied. Reading it
        // eagerly here would bind the data source to the appsettings.json
        // placeholder and integration tests' Testcontainers override would
        // never take effect.
        // The `__PROVIDED_AT_RUNTIME__` sentinel in appsettings.json fails
        // fast at first resolve when the environment hasn't supplied a real
        // value via Aspire reference, env var, user-secrets, or Key Vault.
        services.AddSingleton<NpgsqlDataSource>(sp =>
        {
            const string Placeholder = "__PROVIDED_AT_RUNTIME__";
            var cfg = sp.GetRequiredService<IConfiguration>();
            var cs = cfg.GetConnectionString("agenticworkforce");
            if (string.IsNullOrWhiteSpace(cs)
                || string.Equals(cs, Placeholder, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "Connection string 'agenticworkforce' is required (set via Aspire reference, "
                    + "environment variable, user-secrets, or Key Vault — never hardcoded in appsettings.json).");
            return DataSourceFactory.Create(cs);
        });

        // IConnectionMultiplexer is a singleton (the StackExchange.Redis
        // guidance is one multiplexer per app, NOT per call). Lazy factory
        // mirrors the NpgsqlDataSource pattern so WebApplicationFactory's
        // config overrides reach the connection string.
        // The same multiplexer backs the SignalR backplane registered in
        // Api/Program.cs, our IRedisPubSubService, and Phase-5+
        // RedisIdempotencyService — one connection pool for all three.
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var cs = cfg.GetConnectionString("redis");
            if (string.IsNullOrWhiteSpace(cs))
                throw new InvalidOperationException(
                    "Connection string 'redis' is required (Aspire reference, env var, or Key Vault).");
            return ConnectionMultiplexer.Connect(cs);
        });

        services.AddSingleton<IRedisPubSubService, RedisPubSubService>();
        // RedisEventPublisher is scoped because it holds an AppDbContext.
        services.AddScoped<IEventPublisher, RedisEventPublisher>();

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
        services.AddScoped<IPlatformStatsRepository, PlatformStatsRepository>();

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
