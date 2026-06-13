using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NUPAL.Core.Application.Interfaces;
using System.Text.Json;

namespace NUPAL.Core.Infrastructure.Services;

public class RedisCacheService : ICacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisCacheService> _logger;

        private static readonly JsonSerializerOptions RedisJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

    public RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        try
        {
            var bytes = await _cache.GetAsync(key);
            if (bytes is null) return null;
            return JsonSerializer.Deserialize<T>(bytes, RedisJsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] GET failed for key '{Key}' — cache miss fallback", key);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(string key, T value, TimeSpan expiry) where T : class
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, RedisJsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiry
            };
            await _cache.SetAsync(key, bytes, options);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] SET failed for key '{Key}' — continuing without cache", key);
        }
    }


    public async Task RemoveAsync(string key)
    {
        try
        {
            await _cache.RemoveAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Cache] REMOVE failed for key '{Key}'", key);
        }
    }


    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry) where T : class
    {
        var cached = await GetAsync<T>(key);
        if (cached is not null) return cached;

        var value = await factory();

        if (value is not null)
            await SetAsync(key, value, expiry);

        return value;
    }
}
