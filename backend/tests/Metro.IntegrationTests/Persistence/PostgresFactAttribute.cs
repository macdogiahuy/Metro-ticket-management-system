namespace Metro.IntegrationTests.Persistence;

/// <summary>Database tests require an explicitly selected disposable PostgreSQL database.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("METRO_TEST_CONNECTION")))
            Skip = "Set METRO_TEST_CONNECTION to a disposable database deployed with infra/database/deploy.sql.";
    }
}
