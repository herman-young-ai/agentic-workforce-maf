using Xunit;

namespace AgenticWorkforce.Api.Tests.Integration;

/// <summary>
/// Single xUnit collection that shares one <see cref="ApiWebApplicationFactory"/>
/// (and therefore one Postgres + one Redis container) across every
/// integration test class. Without this, every <c>IClassFixture</c>
/// instantiation spun up its own container pair — 17 classes × 2 containers
/// = 34 concurrent containers under xUnit's default parallelism, which
/// caused intermittent Docker resource pressure and flaky tests.
///
/// <para><b>Trade-off</b></para>
/// Tests within the collection run sequentially. The full integration suite
/// goes from ~25 s (parallel, 17 fixtures) to a slower but stable serial
/// runtime — one container boot instead of seventeen.
///
/// <para><b>Isolation guarantee</b></para>
/// Every test class already uses fresh <see cref="Guid.NewGuid"/> user/project
/// IDs, so the shared database doesn't introduce cross-test contamination.
/// If a future test needs an empty database it must either clean up
/// explicitly or use a private fixture.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationTestCollection : ICollectionFixture<ApiWebApplicationFactory>
{
    public const string Name = "Integration";
}
