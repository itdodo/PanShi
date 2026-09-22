using Microsoft.Extensions.Caching.Memory;

namespace Panshi.Common.Cache;

/// <summary>缓存抽象（默认内存实现；Redis 封装保留、默认关闭——蓝图「明确不引入」清单）。</summary>
public interface ICacheService
{
    T? Get<T>(string key);

    Task<T?> GetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null);

    void Set<T>(string key, T value, TimeSpan? ttl = null);

    void Remove(string key);

    /// <summary>按前缀清除（权限缓存整体失效用）。</summary>
    void RemoveByPrefix(string prefix);
}

/// <summary>IMemoryCache 实现（单机版默认）。</summary>
public sealed class MemoryCacheService(IMemoryCache cache) : ICacheService
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    public T? Get<T>(string key) => cache.TryGetValue<T?>(key, out var v) ? v : default;

    public async Task<T?> GetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null)
    {
        if (cache.TryGetValue<T?>(key, out var hit)) return hit;
        var value = await factory();
        cache.Set(key, value, ttl ?? DefaultTtl);
        return value;
    }

    public void Set<T>(string key, T value, TimeSpan? ttl = null) =>
        cache.Set(key, value, ttl ?? DefaultTtl);

    public void Remove(string key) => cache.Remove(key);

    public void RemoveByPrefix(string prefix)
    {
        // MemoryCache 无枚举 API：用版本号整体失效（与权限缓存版本号方案一致）
        cache.Remove(prefix + ":__version__");
    }
}
