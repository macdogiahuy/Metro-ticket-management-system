using System.Text.Json;
using Metro.Application.Abstractions.Catalog;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Metro.Infrastructure.Caching;

/// <summary>Short-lived Redis/IDistributedCache wrapper with fail-open DB fallback, safe logging, and operator epoch invalidation.</summary>
public sealed class DistributedCatalogCache(
    IDistributedCache cache,
    IOptions<CatalogCacheOptions> options,
    ILogger<DistributedCatalogCache> logger) : ICatalogCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan EpochTtl = TimeSpan.FromHours(24);

    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (!config.Enabled || ttl <= TimeSpan.Zero) return await factory(cancellationToken);

        var fullKey = await ResolveFullKeyAsync(config, key, cancellationToken);
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
        var fullKey = await ResolveFullKeyAsync(config, key, cancellationToken);
        try { await cache.RemoveAsync(fullKey, cancellationToken); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Catalog cache eviction failed for key {CacheKey} ({ExceptionType}).", fullKey, ex.GetType().Name);
        }
    }

    public async Task InvalidateOperatorAsync(Guid operatorId, CancellationToken cancellationToken)
    {
        var config = options.Value;
        if (!config.Enabled || operatorId == Guid.Empty) return;
        var epochKey = $"{config.KeyPrefix}op-epoch:{operatorId:N}";
        var token = Guid.NewGuid().ToString("N")[..12];
        try
        {
            await cache.SetStringAsync(epochKey, token, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = EpochTtl }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Catalog cache invalidation failed for operator {OperatorId} ({ExceptionType}).", operatorId, ex.GetType().Name);
        }
    }

    private async Task<string> ResolveFullKeyAsync(CatalogCacheOptions config, string key, CancellationToken ct)
    {
        if (!TryExtractOperatorId(key, out var operatorId)) return config.KeyPrefix + key;
        var epochKey = $"{config.KeyPrefix}op-epoch:{operatorId:N}";
        try
        {
            var epoch = await cache.GetStringAsync(epochKey, ct);
            return string.IsNullOrEmpty(epoch) ? config.KeyPrefix + key : $"{config.KeyPrefix}e{epoch}:{key}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Catalog epoch read failed for operator {OperatorId} ({ExceptionType}); using unversioned key.", operatorId, ex.GetType().Name);
            return config.KeyPrefix + key;
        }
    }

    private static bool TryExtractOperatorId(string key, out Guid operatorId)
    {
        operatorId = Guid.Empty;
        var first = key.IndexOf(':');
        if (first <= 0 || key.Length < first + 33) return false;
        var span = key.AsSpan(first + 1);
        var next = span.IndexOf(':');
        var token = next >= 0 ? span[..next] : span;
        return token.Length == 32 && Guid.TryParseExact(token, "N", out operatorId);
    }
}
