using System.Text;
using Metro.Application.Abstractions.Catalog;
using Metro.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Metro.IntegrationTests.Caching;

public sealed class DistributedCatalogCacheTests
{
    [Fact]
    public async Task GetOrCreateAsync_CachesHitWithinTtl_AndEvictsOnRemove()
    {
        var distributed = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var sut = new DistributedCatalogCache(
            distributed,
            Options.Create(new CatalogCacheOptions { Enabled = true, KeyPrefix = "test:v1:" }),
            NullLogger<DistributedCatalogCache>.Instance);

        var calls = 0;
        Task<SamplePayload> Factory(CancellationToken _)
        {
            calls++;
            return Task.FromResult(new SamplePayload($"value-{calls}"));
        }

        var first = await sut.GetOrCreateAsync("lines", TimeSpan.FromSeconds(30), Factory, CancellationToken.None);
        var second = await sut.GetOrCreateAsync("lines", TimeSpan.FromSeconds(30), Factory, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal("value-1", first.Name);
        Assert.Equal("value-1", second.Name);
        Assert.NotNull(await distributed.GetAsync("test:v1:lines"));

        await sut.RemoveAsync("lines", CancellationToken.None);
        var third = await sut.GetOrCreateAsync("lines", TimeSpan.FromSeconds(30), Factory, CancellationToken.None);
        Assert.Equal(2, calls);
        Assert.Equal("value-2", third.Name);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenRedisThrows_FailsOpenToFactoryWithoutCrashing()
    {
        var sut = new DistributedCatalogCache(
            new ThrowingDistributedCache(),
            Options.Create(new CatalogCacheOptions { Enabled = true, KeyPrefix = "test:v1:" }),
            NullLogger<DistributedCatalogCache>.Instance);

        var result = await sut.GetOrCreateAsync(
            "service-status",
            TimeSpan.FromSeconds(20),
            _ => Task.FromResult(new SamplePayload("from-db")),
            CancellationToken.None);

        Assert.Equal("from-db", result.Name);
        await sut.RemoveAsync("service-status", CancellationToken.None);
    }

    [Fact]
    public async Task GetOrCreateAsync_WhenDisabledOrZeroTtl_BypassesCache()
    {
        var spy = new SpyDistributedCache();
        var disabled = new DistributedCatalogCache(
            spy,
            Options.Create(new CatalogCacheOptions { Enabled = false }),
            NullLogger<DistributedCatalogCache>.Instance);

        var result = await disabled.GetOrCreateAsync("k", TimeSpan.FromSeconds(60),
            _ => Task.FromResult(new SamplePayload("direct")), CancellationToken.None);

        Assert.Equal("direct", result.Name);
        Assert.Equal(0, spy.GetCount);
        Assert.Equal(0, spy.SetCount);

        var enabled = new DistributedCatalogCache(
            spy,
            Options.Create(new CatalogCacheOptions { Enabled = true }),
            NullLogger<DistributedCatalogCache>.Instance);

        await enabled.GetOrCreateAsync("k", TimeSpan.Zero,
            _ => Task.FromResult(new SamplePayload("zero-ttl")), CancellationToken.None);
        Assert.Equal(0, spy.GetCount);
        Assert.Equal(0, spy.SetCount);
    }

    private sealed record SamplePayload(string Name);

    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new InvalidOperationException("Redis unreachable");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Redis unreachable");
        public void Refresh(string key) => throw new InvalidOperationException("Redis unreachable");
        public Task RefreshAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Redis unreachable");
        public void Remove(string key) => throw new InvalidOperationException("Redis unreachable");
        public Task RemoveAsync(string key, CancellationToken token = default) => throw new InvalidOperationException("Redis unreachable");
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw new InvalidOperationException("Redis unreachable");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw new InvalidOperationException("Redis unreachable");
    }

    private sealed class SpyDistributedCache : IDistributedCache
    {
        public int GetCount { get; private set; }
        public int SetCount { get; private set; }
        public byte[]? Get(string key) { GetCount++; return null; }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) { GetCount++; return Task.FromResult<byte[]?>(null); }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public Task RemoveAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { SetCount++; }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            SetCount++;
            return Task.FromResult(Encoding.UTF8.GetBytes("ok"));
        }
    }
}
