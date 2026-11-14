using System.Diagnostics;
using System.Text;
using Metro.Application.Abstractions.Storage;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Metro.API.Endpoints;

public static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/diagnostics/db", GetDatabaseAsync)
            .WithName("DiagnosticsDatabase")
            .WithTags("Diagnostics");

        app.MapPost("/api/v1/diagnostics/storage/roundtrip", RoundtripAsync)
            .WithName("DiagnosticsStorageRoundtrip")
            .WithTags("Diagnostics");

        return app;
    }

    private static async Task<IResult> GetDatabaseAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var database = await db.Database
            .SqlQueryRaw<string>("SELECT current_database() AS \"Value\"")
            .SingleAsync(cancellationToken);

        var serverVersion = await db.Database
            .SqlQueryRaw<string>("SELECT current_setting('server_version') AS \"Value\"")
            .SingleAsync(cancellationToken);

        var tables = await db.Database
            .SqlQueryRaw<int>(
                "SELECT count(*)::int AS \"Value\" FROM information_schema.tables WHERE table_schema = 'metro'")
            .SingleAsync(cancellationToken);

        var migrations = await db.Database
            .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM metro.schema_migrations")
            .SingleAsync(cancellationToken);

        return Results.Ok(new
        {
            provider = "PostgreSQL (Neon)",
            database,
            serverVersion,
            schema = "metro",
            tables,
            migrations,
        });
    }

    private static async Task<IResult> RoundtripAsync(
        IObjectStorage storage,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("Diagnostics.Storage");
        var key = $"diagnostics/roundtrip-{Guid.NewGuid():N}.txt";
        var payload = Encoding.UTF8.GetBytes($"metro-neon-roundtrip {DateTimeOffset.UtcNow:O}");
        var stopwatch = Stopwatch.StartNew();
        var downloaded = false;
        var matches = false;
        var deleted = false;

        try
        {
            await storage.PutAsync(key, new MemoryStream(payload), "text/plain", cancellationToken);

            await using var stream = await storage.GetAsync(key, cancellationToken);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            downloaded = true;
            matches = buffer.ToArray().AsSpan().SequenceEqual(payload);
        }
        finally
        {
            try
            {
                await storage.DeleteAsync(key, cancellationToken);
                deleted = true;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not delete roundtrip object {Key}", key);
            }
        }

        stopwatch.Stop();

        return Results.Ok(new
        {
            bucket = storage.Bucket,
            key,
            bytes = payload.Length,
            uploaded = true,
            downloaded,
            matches,
            deleted,
            durationMs = stopwatch.ElapsedMilliseconds,
        });
    }
}
