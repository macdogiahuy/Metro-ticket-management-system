using System.Text.Json;
using Metro.Application.Abstractions.Catalog;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Metro.Infrastructure.Caching;

/// <summary>Short-lived Redis/IDistributedCache wrapper with fail-open DB fallback and safe logging.</summary>
public sealed class DistributedCatalogCache(
    IDistributedCache cache,
    IOptions<CatalogCacheOptions> options,
    ILogger<DistributedCatalogCache> logger) : ICatalogCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (!config.Enabled || ttl <= TimeSpan.Zero) return await factory(cancellationToken);

        var fullKey = config.KeyPrefix + key;
        try
        {
            var cached = await cache.GetStringAsync(fullKey, cancellationToken);
            if (!string.IsNullOrEmpty(cached) && JsonSerializer.Deserialize<T>(cached, JsonOptions) is { } hit)
                return hit;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Catalog cache read failed for key {CacheKey} ({ExceptionType}); falling back to DB.", fullKey, ex.GetType().Name);
        }

        var value = await factory(cancellationToken);
        try
        {
            await cache.SetStringAsync(fullKey, JsonSerializer.Serialize(value, JsonOptions),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Catalog cache write failed for key {CacheKey} ({ExceptionType}); continuing uncached.", fullKey, ex.GetType().Name);
        }

        return value;
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (!config.Enabled) return;
        var fullKey = config.KeyPrefix + key;
        try { await cache.RemoveAsync(fullKey, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Catalog cache eviction failed for key {CacheKey} ({ExceptionType}).", fullKey, ex.GetType().Name);
        }
    }
}
