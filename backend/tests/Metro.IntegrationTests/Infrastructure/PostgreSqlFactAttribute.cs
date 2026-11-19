namespace Metro.IntegrationTests.Infrastructure;

/// <summary>Marks a test that must run against a dedicated PostgreSQL database.</summary>
public sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (!PostgreSqlTestConfiguration.IsConfigured
            && Environment.GetEnvironmentVariable(PostgreSqlTestConfiguration.RequiredVariable) != "1")
        {
            Skip = $"Set {PostgreSqlTestConfiguration.ConnectionVariable} to a dedicated metro_test_ database.";
        }
    }
}
