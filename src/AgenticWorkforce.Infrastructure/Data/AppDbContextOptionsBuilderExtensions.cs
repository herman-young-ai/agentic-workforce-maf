using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace AgenticWorkforce.Infrastructure.Data;

/// <summary>
/// Single source of truth for the <see cref="AppDbContext"/> wiring used by
/// both production (<c>AddInfrastructure</c>) and integration tests
/// (<c>ApiWebApplicationFactory</c>). Keeping this in one helper prevents the
/// two paths from drifting — they have done so before, and the failure mode
/// (mismatched enum mappings between hosts) is silent until it explodes at
/// model finalization.
/// </summary>
public static class AppDbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseAgenticWorkforce(
        this DbContextOptionsBuilder options,
        NpgsqlDataSource dataSource,
        IInterceptor auditInterceptor)
    {
        return options
            .UseNpgsql(dataSource, npgsql =>
            {
                npgsql.EnableRetryOnFailure(3);
                npgsql.UseVector();
                // Register every domain enum with EF Core's type-mapping source so
                // CLR enum properties resolve to NpgsqlEnumTypeMapping<T>. The matching
                // EnableUnmappedTypes() on DataSourceFactory handles wire encoding.
                // See docs/002-architecture/003-database-schema.md §4.1.
                foreach (var (clrType, pgEnumName) in PgEnumRegistry.All)
                {
                    npgsql.MapEnum(clrType, pgEnumName);
                }
            })
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(auditInterceptor);
    }
}
