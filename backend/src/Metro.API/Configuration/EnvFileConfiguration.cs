using Microsoft.Extensions.Configuration;

namespace Metro.API.Configuration;

/// <summary>
/// Minimal reader for the env file that <c>neon deploy</c> writes (.env.local, or .env).
/// Real environment variables keep precedence so CI and production never read a stale file.
/// </summary>
public static class EnvFileConfiguration
{
    public static void LoadNearest(IConfigurationBuilder configuration, string? startDirectory = null)
    {
        var directory = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());

        while (directory is not null)
        {
            foreach (var fileName in new[] { ".env.local", ".env" })
            {
                var path = Path.Combine(directory.FullName, fileName);
                if (File.Exists(path))
                {
                    configuration.AddInMemoryCollection(Parse(path));
                    return;
                }
            }

            directory = directory.Parent;
        }
    }

    internal static Dictionary<string, string?> Parse(string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].TrimStart();
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = Unquote(line[(separator + 1)..].Trim());

            // A real environment variable wins over the local file.
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
