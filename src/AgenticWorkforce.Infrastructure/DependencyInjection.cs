using AgenticWorkforce.Domain.Interfaces.Repositories;
using AgenticWorkforce.Domain.Interfaces.Services;
using AgenticWorkforce.Infrastructure.Data;
using AgenticWorkforce.Infrastructure.Repositories;
using AgenticWorkforce.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticWorkforce.Infrastructure;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string? documentStoreBasePath = null)
    {
        var dataSource = DataSourceFactory.Create(connectionString);

        services.AddScoped<AuditInterceptor>();

        services.AddDbContext<AppDbContext>((sp, opts) =>
            opts.UseAgenticWorkforce(dataSource, sp.GetRequiredService<AuditInterceptor>()));

        // Aggregate-root repositories only — non-aggregate queries hit AppDbContext
        // directly from vertical-slice handlers.
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();

        // Service stubs — replaced in Phase 6+ (embedding provider) and Phase 11 (blob storage)
        services.AddScoped<IEmbeddingService, StubEmbeddingService>();
        var docRoot = documentStoreBasePath ?? Path.Combine(Path.GetTempPath(), "agenticworkforce-docs");
        Directory.CreateDirectory(docRoot);
        services.AddScoped<IDocumentStore>(_ => new LocalFileDocumentStore(docRoot));

        return services;
    }
}
