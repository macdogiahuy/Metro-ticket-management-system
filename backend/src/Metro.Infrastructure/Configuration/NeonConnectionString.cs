using Npgsql;

namespace Metro.Infrastructure.Configuration;

/// <summary>
/// Neon injects <c>DATABASE_URL</c> as a libpq URI (postgresql://user:password@host/db?sslmode=require).
/// Npgsql expects a keyword/value connection string, so translate it before building the data source.
/// </summary>
public static class NeonConnectionString
{
    public static string Resolve(string? databaseUrl, string? fallbackConnectionString)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            return fallbackConnectionString
                ?? throw new InvalidOperationException(
                    "No database configured. Set DATABASE_URL (Neon) or ConnectionStrings:DefaultConnection.");
        }

        if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != "postgres" && uri.Scheme != "postgresql"))
        {
            // Already a keyword/value connection string.
            return databaseUrl;
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var query = ParseQuery(uri.Query);

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/')),
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : null,
            ApplicationName = "Metro.API",
            Pooling = true,
            MaxPoolSize = 20,
            Timeout = 15,
            CommandTimeout = 30,
        };

        builder.SslMode = query.TryGetValue("sslmode", out var sslMode)
            && Enum.TryParse<SslMode>(sslMode, ignoreCase: true, out var parsedSslMode)
                ? parsedSslMode
                : SslMode.Require;

        // Neon requires channel binding; Npgsql enforces it when asked, and ignores it otherwise.
        if (query.TryGetValue("channel_binding", out var channelBinding)
            && Enum.TryParse<ChannelBinding>(channelBinding, ignoreCase: true, out var parsedChannelBinding))
        {
            builder.ChannelBinding = parsedChannelBinding;
        }

        return builder.ConnectionString;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return values;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            values[Uri.UnescapeDataString(parts[0])] =
                parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
        }

        return values;
    }
}
