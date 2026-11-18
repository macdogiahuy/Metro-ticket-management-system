namespace Metro.IntegrationTests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DatabaseIntegrationCollection : ICollectionFixture<MetroWebApplicationFactory>
{
    public const string Name = "PostgreSQL API";
}
