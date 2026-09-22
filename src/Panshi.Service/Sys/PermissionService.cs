using Panshi.Common.Cache;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;

namespace Panshi.Service.Sys;

/// <summary>用户认证数据（角色码+权限码+超管标记），进程缓存。</summary>
public sealed record UserAuth(long UserId, IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions, bool IsSuper, long? DeptId, string UserName);

/// <summary>
/// 权限集合查询与缓存（蓝图§5.4）：进程缓存 + 版本号整体失效——
/// 任何角色菜单授权/用户角色变更调 InvalidateAll()，下一请求即重建。
/// </summary>
public class PermissionService(
    IRepository<SysUser> userRepo,
    IRepository<SysUserRole> userRoleRepo,
    IRepository<SysRole> roleRepo,
    IRepository<SysRoleMenu> roleMenuRepo,
    IRepository<SysMenu> menuRepo,
    ICacheService cache)
{
    public const string SuperRoleCode = "admin";

    private static long _version = 1;

    public static void InvalidateAll() => Interlocked.Increment(ref _version);

    public async Task<UserAuth?> GetAuthAsync(long userId)
    {
        var key = $"perm:auth:{userId}:{_version}";
        return await cache.GetAsync(key, () => LoadAsync(userId));
    }

    public async Task<bool> HasPermissionAsync(long userId, string code)
    {
        var auth = await GetAuthAsync(userId);
        return auth is not null && (auth.IsSuper || auth.Permissions.Contains(code, StringComparer.Ordinal));
    }

    private async Task<UserAuth> LoadAsync(long userId)
    {
        var user = await userRepo.FindAsync(userId)
            ?? throw new KeyNotFoundException($"用户 {userId} 不存在");

        var links = await userRoleRepo.ListAsync(l => l.UserId == userId);
        var roleIds = links.Select(l => l.RoleId).ToHashSet();
        var roles = (await roleRepo.ListAsync())
            .Where(r => roleIds.Contains(r.Id) && r.Status == EnableStatus.Enabled)
            .Select(r => r.RoleCode).ToList();

        var isSuper = roles.Contains(SuperRoleCode) || user.UserName == AuthService.AdminUserName;
        var menus = await menuRepo.ListAsync();

        if (isSuper)
            return new UserAuth(user.Id, roles, menus.Where(m => !string.IsNullOrEmpty(m.Permission)).Select(m => m.Permission!)
                .Distinct().ToList(), true, user.DeptId, user.UserName);

        var roleMenus = await roleMenuRepo.ListAsync(rm => roleIds.Contains(rm.RoleId));
        var menuIds = roleMenus.Select(rm => rm.MenuId).ToHashSet();
        var perms = menus.Where(m => menuIds.Contains(m.Id) && !string.IsNullOrEmpty(m.Permission) &&
                                      m.Status == EnableStatus.Enabled)
            .Select(m => m.Permission!).Distinct().ToList();
        return new UserAuth(user.Id, roles, perms, false, user.DeptId, user.UserName);
    }
}
