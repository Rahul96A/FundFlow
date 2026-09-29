using System.Text.Json;
using FundFlow.Application.Common.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FundFlow.Infrastructure.Caching;

/// <summary>
/// JSON cache over <see cref="IDistributedCache"/> (Redis in real deployments, in-memory in tests).
/// A cache outage must not take the API down: failures are logged and behave like a miss / no-op.
/// (The trade-off is that a session deny-list entry can be missed for at most one access-token lifetime.)
/// </summary>
public sealed class CacheService(IDistributedCache cache, ILogger<CacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var bytes = await cache.GetAsync(key, cancellationToken);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache read failed for {CacheKey}; treating as a miss", key);
            return null;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan timeToLive, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            await cache.SetAsync(
                key,
                JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = timeToLive },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache write failed for {CacheKey}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            await cache.RemoveAsync(key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache removal failed for {CacheKey}", key);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            return await cache.GetAsync(key, cancellationToken) is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cache lookup failed for {CacheKey}; treating as absent", key);
            return false;
        }
    }
}
