using Panshi.Common.Cache;
using Panshi.Model.Entities;
using Panshi.Repository;
using Panshi.Service.Base;

namespace Panshi.Service.Sys;

/// <summary>系统参数服务（全量字典缓存；保存即整体失效）。</summary>
public class ConfigService(IRepository<SysConfig> repo, ICacheService cache) : BaseService<SysConfig>(repo)
{
    private const string CacheKey = "sys:config:all";

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        return await cache.GetAsync(CacheKey, async () =>
            (await Repo.ListAsync()).Where(c => !string.IsNullOrEmpty(c.ConfigValue))
                .GroupBy(c => c.ConfigKey)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.UpdateTime ?? x.CreateTime)
                    .First().ConfigValue!)) ?? [];
    }

    public async Task<string> GetAsync(string key, string defaultValue = "")
        => (await GetAllAsync()).TryGetValue(key, out var v) ? v : defaultValue;

    public async Task<int> GetIntAsync(string key, int defaultValue)
        => int.TryParse(await GetAsync(key), out var i) ? i : defaultValue;

    public async Task<bool> GetBoolAsync(string key, bool defaultValue)
        => await GetAsync(key, defaultValue ? "1" : "0") is "1" or "true";

    /// <summary>内置参数键（管理端禁删）。</summary>
    public static readonly string[] BuiltInKeys =
    [
        "sys.user.initPassword", "sys.login.failLimit", "sys.login.lockMinutes",
        "sys.captcha.enabled", "sys.pwd.expireDays", "sys.login.kickSameUser"
    ];

    public void Invalidate() => cache.Remove(CacheKey);
}
