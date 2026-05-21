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

        // Aggregate-root query repositories. Writes go through AppDbContext
        // directly from vertical-slice handlers.
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();

        // Service stubs — replaced in Phase 6+ (embedding provider) and Phase 11 (blob storage)
        services.AddScoped<IEmbeddingService, StubEmbeddingService>();

        var docRoot = configuration["DocumentStore:BasePath"]
            ?? Path.Combine(Path.GetTempPath(), "agenticworkforce-docs");
        Directory.CreateDirectory(docRoot);
        services.AddScoped<IDocumentStore>(_ => new LocalFileDocumentStore(docRoot));

        return services;
    }
}
