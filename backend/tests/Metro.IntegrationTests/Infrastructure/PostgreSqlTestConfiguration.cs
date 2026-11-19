using Npgsql;

namespace Metro.IntegrationTests.Infrastructure;

internal sealed record PostgreSqlTestConfiguration(string ConnectionString, string DatabaseName)
{
    internal const string ConnectionVariable = "METRO_TEST_POSTGRES_CONNECTION_STRING";
    internal const string DatabaseVariable = "METRO_TEST_POSTGRES_DATABASE";
    internal const string RequiredVariable = "METRO_TEST_POSTGRES_REQUIRED";

    internal static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable));

    internal static PostgreSqlTestConfiguration LoadRequired()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"{ConnectionVariable} is required for PostgreSQL integration tests.");

        NpgsqlConnectionStringBuilder settings;
        try
        {
            settings = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("The PostgreSQL test connection string is invalid.", exception);
        }

        var expectedDatabase = Environment.GetEnvironmentVariable(DatabaseVariable);
        if (string.IsNullOrWhiteSpace(expectedDatabase)
            || !expectedDatabase.StartsWith("metro_test_", StringComparison.Ordinal)
            || !string.Equals(settings.Database, expectedDatabase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"The test database must match {DatabaseVariable} and its name must start with metro_test_.");
        }

        settings.IncludeErrorDetail = false;
        settings.Timeout = 10;
        return new PostgreSqlTestConfiguration(settings.ConnectionString, expectedDatabase);
    }
}
