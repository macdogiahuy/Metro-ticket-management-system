using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Metro.Application.Abstractions.Fare;
using Microsoft.Extensions.Caching.Distributed;

namespace Metro.API.Filters;

/// <summary>Idempotency-Key filter for write endpoints: replays identical 2xx responses and rejects payload conflicts with 409.</summary>
public sealed class IdempotencyEndpointFilter(IDistributedCache cache) : IEndpointFilter
{
    private const string HeaderName = "Idempotency-Key";
    private const int MaxKeyLength = 128;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (!http.Request.Headers.TryGetValue(HeaderName, out var rawValues))
            return await next(context);

        var rawKey = rawValues.ToString().Trim();
        if (string.IsNullOrEmpty(rawKey) || rawKey.Length > MaxKeyLength)
            throw new FareException("INVALID_IDEMPOTENCY_KEY", StatusCodes.Status400BadRequest,
                $"Header {HeaderName} must be 1..{MaxKeyLength} non-whitespace characters.");

        var actor = http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? http.User.FindFirstValue("sub") ?? "anon";
        var keyHash = Sha256Hex($"{actor}:{http.Request.Method}:{http.Request.Path}:{rawKey}");
        var cacheKey = $"idem:v1:{keyHash}";
        var payloadHash = ComputePayloadHash(http.Request.Method, http.Request.Path.Value ?? string.Empty, context.Arguments);
        var ct = http.RequestAborted;

        var gate = Locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var existingJson = await cache.GetStringAsync(cacheKey, ct);
            if (!string.IsNullOrEmpty(existingJson) && JsonSerializer.Deserialize<StoredResponse>(existingJson, JsonOptions) is { } stored)
            {
                if (!string.Equals(stored.PayloadHash, payloadHash, StringComparison.Ordinal))
                    throw new FareException("IDEMPOTENCY_KEY_CONFLICT", StatusCodes.Status409Conflict,
                        "Idempotency-Key was already used with a different request payload.");

                http.Response.Headers["X-Idempotency-Replayed"] = "true";
                if (!string.IsNullOrEmpty(stored.Location))
                    http.Response.Headers.Location = stored.Location;
                return stored.BodyJson is null
                    ? Results.StatusCode(stored.StatusCode)
                    : Results.Content(stored.BodyJson, "application/json", Encoding.UTF8, stored.StatusCode);
            }

            var result = await next(context);
            if (result is IStatusCodeHttpResult { StatusCode: >= 200 and < 300 } statusResult)
            {
                var bodyJson = result is IValueHttpResult valueResult && valueResult.Value is not null
                    ? JsonSerializer.Serialize(valueResult.Value, JsonOptions) : null;
                var location = result.GetType().GetProperty("Location")?.GetValue(result) as string;
                var record = new StoredResponse(payloadHash, statusResult.StatusCode.Value, bodyJson, location);
                await cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(record, JsonOptions),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl }, ct);
            }
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private static string ComputePayloadHash(string method, string path, IList<object?> arguments)
    {
        var data = arguments.Where(IsRequestData).ToArray();
        var serialized = JsonSerializer.Serialize(new { method, path, data }, JsonOptions);
        return Sha256Hex(serialized);
    }

    private static bool IsRequestData(object? arg) =>
        arg is not null && arg is not CancellationToken && arg is not HttpContext && arg is not ClaimsPrincipal
        && (arg.GetType().IsValueType || arg is string || arg.GetType().Namespace?.StartsWith("Metro.Application.", StringComparison.Ordinal) == true);

    private static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

    private sealed record StoredResponse(string PayloadHash, int StatusCode, string? BodyJson, string? Location);
}
