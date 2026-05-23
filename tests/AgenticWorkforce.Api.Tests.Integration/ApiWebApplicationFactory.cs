using AgenticWorkforce.Api.Core.Auth;
using AgenticWorkforce.Domain.Entities;
using AgenticWorkforce.Domain.Enums;
using AgenticWorkforce.Infrastructure.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

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

    // Phase 5: a real Redis is required for IConnectionMultiplexer + SignalR
    // backplane + RedisIdempotencyService + RedisEventPublisher to start.
    // Project policy is no mocks (Principle: real dependencies in tests).
    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public async Task StartAsync()
    {
        // Both can boot concurrently — they share nothing.
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());
    }

    public string ConnectionString => _postgres.GetConnectionString();
    public string RedisConnectionString => _redis.GetConnectionString();

    /// <summary>
    /// Per-test-run scratch directory for LocalFileDocumentStore. Avoids
    /// /tmp pollution and lets parallel test classes use isolated roots.
    /// </summary>
    private readonly string _documentStoreRoot = Path.Combine(
        Path.GetTempPath(),
        $"awf-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(cfg =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:agenticworkforce"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:redis"]            = _redis.GetConnectionString(),
                ["DocumentStore:BasePath"]             = _documentStoreRoot
            }));

        // Override JWT auth with the test auth handler so tests don't need real tokens
        builder.ConfigureTestServices(services =>
            services.AddAuthentication(defaultScheme: TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { }));
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> that authenticates every request as the given test user.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(Guid userId, string email, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User-Id",    userId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-User-Email", email);
        if (roles.Length > 0)
            client.DefaultRequestHeaders.Add("X-Test-User-Roles", string.Join(",", roles));
        return client;
    }

    /// <summary>
    /// Inserts a <see cref="User"/> row so FK constraints on <c>ProjectMember.UserId</c> are satisfied.
    /// Must be called before any API call that creates a project (which auto-adds an owner member).
    /// </summary>
    public async Task<User> SeedUserAsync(Guid userId, string email, SystemRole systemRole = SystemRole.Member)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var existing = await db.Users.FindAsync(userId);
        if (existing is not null)
            return existing;

        var user = new User
        {
            Id          = userId,
            Email       = email,
            DisplayName = email,
            SystemRole  = systemRole,
            IsActive    = true
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public override async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
        await base.DisposeAsync();

        // Best-effort cleanup of the per-run document scratch dir.
        // Tests share nothing here; if a previous run crashed, leaking
        // a few KB of files in /tmp is acceptable.
        try
        {
            if (Directory.Exists(_documentStoreRoot))
                Directory.Delete(_documentStoreRoot, recursive: true);
        }
        catch (IOException) { /* leave the bytes behind */ }
        catch (UnauthorizedAccessException) { /* leave the bytes behind */ }

        GC.SuppressFinalize(this);
    }
}
