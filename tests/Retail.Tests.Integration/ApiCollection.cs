namespace Retail.Tests.Integration;

/// <summary>
/// Shares ONE <see cref="ApiFactory"/> (and thus a single SQL Server container) across
/// every integration test class in the "api" collection, and runs those classes
/// sequentially. This avoids starting a container per class and the resource
/// contention of multiple SQL Server containers racing in parallel on one machine.
/// Tests stay isolated by using unique emails/SKUs rather than separate databases.
/// </summary>
[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
}
