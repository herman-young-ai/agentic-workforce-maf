using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace AgenticWorkforce.Api.Tests.Integration;

/// <summary>
/// Integration test factory using Testcontainers for real PostgreSQL.
/// No InMemory database — tests hit real PostgreSQL (per coding standards).
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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            // Remove existing DbContext registration
            var dbDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (dbDescriptor != null)
                services.Remove(dbDescriptor);

            // Replace with Testcontainers PostgreSQL
            services.AddDbContext<AppDbContext>(opts =>
                opts.UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                {
                    npgsql.EnableRetryOnFailure(3);
                    npgsql.UseVector();
                }));
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
