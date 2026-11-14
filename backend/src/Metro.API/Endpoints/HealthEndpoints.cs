using System.Diagnostics;
using Metro.Application.Abstractions.Storage;
using Metro.Infrastructure.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace Metro.API.Endpoints;

public static class HealthEndpoints
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", GetHealthAsync)
            .WithName("Health")
            .WithTags("Health");

        app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }))
            .WithName("Liveness")
            .WithTags("Health");

        return app;
    }

    private static async Task<IResult> GetHealthAsync(
        ApplicationDbContext db,
        IObjectStorage storage,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        var services = new Dictionary<string, object>();

        var database = await ProbeAsync(
            async ct =>
            {
                var migrations = await db.Database
                    .SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM metro.schema_migrations")
                    .SingleAsync(ct);

                return $"schema migrations: {migrations}";
            },
            timeout.Token);

        services["postgres"] = database;

        var objectStorage = await ProbeAsync(
            async ct =>
            {
                await storage.IsReachableAsync(ct);
                return $"bucket: {storage.Bucket}";
            },
            timeout.Token);

        services["objectStorage"] = objectStorage;

        var healthy = database.Status == "healthy" && objectStorage.Status == "healthy";

        return Results.Json(
            new
            {
                status = healthy ? "healthy" : "unhealthy",
                services,
                checkedAt = DateTimeOffset.UtcNow,
            },
            statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<ServiceProbe> ProbeAsync(
        Func<CancellationToken, Task<string>> probe,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var detail = await probe(cancellationToken);
            stopwatch.Stop();

            return new ServiceProbe("healthy", stopwatch.ElapsedMilliseconds, detail);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            stopwatch.Stop();

            // Never surface connection strings, hostnames or credentials to the caller.
            return new ServiceProbe("unhealthy", stopwatch.ElapsedMilliseconds, Sanitize(exception));
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new ServiceProbe("unhealthy", stopwatch.ElapsedMilliseconds, "probe timed out");
        }
    }

    private static string Sanitize(Exception exception) =>
        exception switch
        {
            TimeoutException => "probe timed out",
            _ => exception.GetType().Name,
        };

    private sealed record ServiceProbe(string Status, long LatencyMs, string Detail);
}
