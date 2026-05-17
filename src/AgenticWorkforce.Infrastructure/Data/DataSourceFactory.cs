using Npgsql;

namespace AgenticWorkforce.Infrastructure.Data;

/// <summary>
/// Builds an <see cref="NpgsqlDataSource"/> with pgvector enabled and generic
/// unmapped-type support turned on so the Npgsql connector can serialize CLR
/// enums against native PG enum columns without per-type <c>MapEnum&lt;T&gt;</c>
/// registrations. EF Core-side enum type mapping is handled by
/// <see cref="InfrastructureServiceExtensions.AddInfrastructure"/> via
/// <c>NpgsqlDbContextOptionsBuilder.MapEnum</c>.
/// <para>
/// Calling <c>MapEnum&lt;T&gt;</c> on BOTH the data source builder and the EF
/// options builder makes Npgsql.EFCore's type-mapping source find duplicate
/// matches and throw <c>Sequence contains more than one matching element</c>
/// at model finalization — keep the registration on the options builder only.
/// </para>
/// </summary>
public static class DataSourceFactory
{
    public static NpgsqlDataSource Create(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.UseVector();
        builder.EnableUnmappedTypes();
        return builder.Build();
    }
}
