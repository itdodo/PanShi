using System.Linq.Expressions;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;

namespace Panshi.Service.Base;

/// <summary>数据权限上下文（多角色取最宽档；预解析为常量集合，查询表达式内零方法调用——红线 #1）。</summary>
public sealed record ScopeCtx(long UserId, long? DeptId, DataScopeType Best, IReadOnlyList<long> DeptIds);

/// <summary>
/// 数据权限五档（蓝图§5.4）：All 全部 / Dept 本部门 / DeptAndChild 本部门及以下 / Self 仅本人 / Custom 自定义。
/// 业务侧用法：repo.PageAsync(And(where, DataScope.Filter&lt;T&gt;(ctx)))——实体实现 IDataScope 即接管。
/// </summary>
public class DataScopeService(
    IRepository<SysUser> userRepo,
    IRepository<SysUserRole> userRoleRepo,
    IRepository<SysRole> roleRepo,
    IRepository<SysRoleDept> roleDeptRepo,
    IRepository<SysDept> deptRepo)
{
    /// <summary>解析当前用户数据权限上下文（超管=null=不过滤）。</summary>
    public async Task<ScopeCtx?> ResolveAsync(long userId)
    {
        var user = await userRepo.FindAsync(userId);
        if (user is null) return null;

        var links = await userRoleRepo.ListAsync(l => l.UserId == userId);
        var roleIds = links.Select(l => l.RoleId).ToHashSet();
        var roles = (await roleRepo.ListAsync())
            .Where(r => roleIds.Contains(r.Id) && r.Status == EnableStatus.Enabled).ToList();

        if (roles.Count == 0)
            return new ScopeCtx(userId, user.DeptId, DataScopeType.Self, []);

        // 多角色并集=取最宽档（All 直接放行）
        var best = roles.Min(r => (int)r.DataScope);
        var scope = (DataScopeType)best;
        if (scope == DataScopeType.All) return null;

        var deptIds = new List<long>();
        if (scope == DataScopeType.DeptAndChild && user.DeptId is long self)
        {
            deptIds = await DescendantsAsync(self);
        }
        else if (scope == DataScopeType.Custom)
        {
            var customLinks = await roleDeptRepo.ListAsync(rd => roleIds.Contains(rd.RoleId));
            deptIds = customLinks.Select(rd => rd.DeptId).Distinct().ToList();
        }

        return new ScopeCtx(userId, user.DeptId, scope, deptIds);
    }

    /// <summary>构建过滤表达式（ctx=null 恒真）。只允许常量比较，满足 SqlSugar 翻译。</summary>
    public static Expression<Func<T, bool>> Filter<T>(ScopeCtx? ctx) where T : IDataScope
    {
        if (ctx is null) return _ => true;

        return ctx.Best switch
        {
            // 无部门用户看不到「本部门/及以下/自定义」数据：用 -1 常量保证空集
            DataScopeType.Dept => it => it.DeptId == (ctx.DeptId ?? -1),
            DataScopeType.DeptAndChild => it => it.DeptId != null && ctx.DeptIds.Contains(it.DeptId.Value),
            DataScopeType.Custom => it => it.DeptId != null && ctx.DeptIds.Contains(it.DeptId.Value),
            _ => it => it.OwnerUserId == ctx.UserId
        };
    }

    /// <summary>部门及子孙（ancestors 链内存 BFS）。</summary>
    private async Task<List<long>> DescendantsAsync(long deptId)
    {
        var all = await deptRepo.ListAsync();
        var result = new List<long> { deptId };
        var frontier = new List<long> { deptId };
        while (frontier.Count > 0)
        {
            var parents = frontier;
            frontier = all.Where(d => d.ParentId is long p && parents.Contains(p))
                .Select(d => d.Id).Except(result).ToList();
            result.AddRange(frontier);
        }

        return result;
    }
}
