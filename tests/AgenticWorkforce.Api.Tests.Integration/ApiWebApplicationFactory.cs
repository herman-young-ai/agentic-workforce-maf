using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AgenticWorkforce.Api.Tests.Integration;

/// <summary>
/// Integration test factory using Testcontainers for real PostgreSQL + pgvector.
/// No InMemory database — tests hit a real Postgres instance through the same
/// <see cref="DataSourceFactory"/> the production code path uses, so enum
/// mappings and pgvector wiring are exercised identically.
/// </summary>
public class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public async Task StartAsync()
    {
        await _postgres.StartAsync();
    }

    public string ConnectionString => _postgres.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Strip every production registration that the host's AddInfrastructure
            // added for AppDbContext so we can re-bind to Testcontainers without
            // double-running the configuration callback (which would call MapEnum
            // twice and trip Npgsql's type-mapping source). The AuditInterceptor,
            // repositories, and service stubs registered by AddInfrastructure
            // stay in place.
            var toRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || d.ServiceType == typeof(AppDbContext)
                         || d.ServiceType == typeof(IDbContextOptionsConfiguration<AppDbContext>))
                .ToList();
            foreach (var d in toRemove)
                services.Remove(d);

            var dataSource = DataSourceFactory.Create(_postgres.GetConnectionString());
            services.AddDbContext<AppDbContext>((sp, opts) =>
                opts.UseAgenticWorkforce(dataSource, sp.GetRequiredService<AuditInterceptor>()));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
