namespace NUPAL.Core.Application.Interfaces;

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key) where T : class;

    Task SetAsync<T>(string key, T value, TimeSpan expiry) where T : class;

    Task RemoveAsync(string key);

    Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan expiry) where T : class;
}
