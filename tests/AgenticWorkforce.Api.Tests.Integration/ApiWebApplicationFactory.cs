using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;

namespace AgenticWorkforce.Api.Tests.Integration;

/// <summary>
/// Integration test factory using Testcontainers for real PostgreSQL + pgvector.
/// The Testcontainers connection string is published to configuration before
/// <c>AddInfrastructure</c> runs, so the production wiring path executes
/// unchanged — no need to strip and re-bind DbContext registrations in DI.
/// </summary>
public class ApiWebApplicationFactory : WebApplicationFactory<Program>, IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public async Task StartAsync() => await _postgres.StartAsync();

    public string ConnectionString => _postgres.GetConnectionString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration(cfg =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:agenticworkforce"] = _postgres.GetConnectionString()
            });
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
