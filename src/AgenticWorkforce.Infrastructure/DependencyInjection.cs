using System.Reflection;
using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Domain.Services;
using AgenticWorkforce.Infrastructure.Data;
using AgenticWorkforce.Infrastructure.Events;
using AgenticWorkforce.Infrastructure.Repositories;
using AgenticWorkforce.Infrastructure.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
        // ProjectEventDispatchInterceptor turns RedisEventPublisher's "Add"
        // calls into a transactional outbox — it dispatches to Redis pub/sub
        // AFTER the SaveChanges commit succeeds. Same scope as AppDbContext.
        services.AddScoped<ProjectEventDispatchInterceptor>();

        services.AddDbContext<AppDbContext>((sp, opts) =>
            opts.UseAgenticWorkforce(
                sp.GetRequiredService<NpgsqlDataSource>(),
                sp.GetRequiredService<AuditInterceptor>(),
                sp.GetRequiredService<ProjectEventDispatchInterceptor>()));

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

        // Agent catalog: caching decorator over the EF Core repository (Phase 6 §7).
        // Both registrations are needed: the inner concrete is resolved by the decorator,
        // and the cached decorator satisfies IAgentCatalogRepository for everyone else.
        services.AddScoped<AgentCatalogRepository>();
        services.AddScoped<IAgentCatalogRepository>(sp =>
            new CachingAgentCatalogRepository(
                sp.GetRequiredService<AgentCatalogRepository>(),
                sp.GetRequiredService<IMemoryCache>()));
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

        // Phase 6 (Agent Runtime) — services and repos that the Agents project consumes.
        services.AddMemoryCache();                       // Backs CachingAgentCatalogRepository.
        services.TryAddSingleton(TimeProvider.System);   // BudgetService + ChatClientFactory + AgentRuntime use this.
        services.AddOptions<BudgetServiceOptions>()
            .Bind(configuration.GetSection(BudgetServiceOptions.SectionName))
            .Validate(o => o.WarningThreshold is > 0 and <= 1,
                "Budget.WarningThreshold must be in the interval (0, 1].");
        services.AddScoped<IBudgetService, BudgetService>();
        services.AddScoped<IModelPricingRepository, ModelPricingRepository>();
        services.AddScoped<IModelPricingService, ModelPricingService>();
        services.AddScoped<ILlmCallRepository, LlmCallRepository>();
        // Token counters: Tiktoken (OpenAI families) + Anthropic (Claude) + Stub (dev pipeline).
        // The router is the public ITokenCounter; concrete counters resolve from the same singleton scope.
        services.AddSingleton<TiktokenTokenCounter>();
        services.AddSingleton<AnthropicTokenCounter>();
        services.AddSingleton<StubTokenCounter>();
        services.AddSingleton<ITokenCounter, TokenCounterRouter>();

        // Stub provider needs a zero-cost ModelPricing row to satisfy the no-fallback rule
        // in ModelPricingService. Gated on AgentRuntime:DefaultProvider == "stub" so real
        // provider deployments never run this seeder.
        if (string.Equals(
                configuration["AgentRuntime:DefaultProvider"],
                "stub",
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddHostedService<StubModelPricingSeeder>();
        }

        // Service stubs — replaced in Phase 6+ (embedding provider) and Phase 11 (blob storage)
        services.AddScoped<IEmbeddingService, StubEmbeddingService>();

        // Phase 7d — platform service-account actor for agent-initiated writes
        // (run_objective, start_research, add_principle). PlatformActorSeeder
        // ensures the User row exists at host startup; PlatformActor reads
        // the configured UUID + email and surfaces them to tools.
        services.AddOptions<PlatformActorOptions>()
            .Bind(configuration.GetSection(PlatformActorOptions.SectionName));
        services.AddSingleton<IPlatformActor, PlatformActor>();
        services.AddHostedService<PlatformActorSeeder>();

        // AgentSeedService is wired by AddAgentSeedingFromAssembly when a host supplies
        // the seed assembly (Worker does, Api does not — Api never runs the seeder).

        // Fail-fast on missing DocumentStore:BasePath, but defer the read
        // into the DI factory so WebApplicationFactory's
        // ConfigureAppConfiguration override applies (same lazy pattern as
        // NpgsqlDataSource and IConnectionMultiplexer above). Reading it
        // here in AddInfrastructure would freeze the appsettings.json
        // sentinel and break every integration test.
        services.AddScoped<IDocumentStore>(sp =>
        {
            const string PlaceholderBasePath = "__PROVIDED_AT_RUNTIME__";
            var cfg = sp.GetRequiredService<IConfiguration>();
            var docRoot = cfg["DocumentStore:BasePath"];
            if (string.IsNullOrWhiteSpace(docRoot)
                || string.Equals(docRoot, PlaceholderBasePath, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "DocumentStore:BasePath is required (set via Aspire reference, env var, "
                    + "user-secrets, or Key Vault — never default to a host temp directory).");
            Directory.CreateDirectory(docRoot);
            return new LocalFileDocumentStore(docRoot);
        });

        return services;
    }

    /// <summary>
    /// Supplies the assembly containing the embedded <c>*.Catalog.Seeds.*.yaml</c>
    /// resources to <see cref="AgentSeedService"/>. The Worker (which references
    /// both Infrastructure and Agents) is the canonical caller; integration tests
    /// can pass a fixture assembly that exposes its own embedded YAMLs.
    /// <para>
    /// Splitting this out preserves the layer rule
    /// <c>Infrastructure ↛ Agents</c> — Infrastructure stays Agents-free; the host
    /// composes the two.
    /// </para>
    /// </summary>
    public static IServiceCollection AddAgentSeedingFromAssembly(
        this IServiceCollection services,
        Assembly seedAssembly)
    {
        ArgumentNullException.ThrowIfNull(seedAssembly);
        services.AddSingleton<IAgentSeedSource>(_ => new EmbeddedYamlAgentSeedSource(seedAssembly));
        services.AddHostedService<AgentSeedService>();
        return services;
    }
}
