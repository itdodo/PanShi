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

    /// <summary>
    /// 日志类表（只有 user_name，没有 DeptId/OwnerUserId，走不了 Filter&lt;T&gt;）的数据权限：
    /// 把五档翻成「可见用户名集合」。返回 null = 不过滤（超管或 All 档）。
    /// ⚠️ 非 All 档因此看不到「不属于任何用户」的行（如撞库不存在的账号产生的失败登录）——保守取舍，宁少不漏。
    /// </summary>
    public async Task<List<string>?> VisibleUserNamesAsync(long userId)
    {
        var ctx = await ResolveAsync(userId);
        if (ctx is null) return null;
        return (await userRepo.ListAsync())
            .Where(u => UserInScope(ctx, u))
            .Select(u => u.UserName)
            .Distinct()
            .ToList();
    }

    /// <summary>某个用户行是否落在 ctx 的可见范围内（用户列表与日志可见名集合共用这一份判定）。</summary>
    public static bool UserInScope(ScopeCtx ctx, SysUser u) => ctx.Best switch
    {
        DataScopeType.Self => u.OwnerUserId == ctx.UserId,
        DataScopeType.Dept => u.DeptId == (ctx.DeptId ?? -1),
        DataScopeType.DeptAndChild => u.DeptId is long d1 && ctx.DeptIds.Contains(d1),
        DataScopeType.Custom => u.DeptId is long d2 && ctx.DeptIds.Contains(d2),
        _ => true
    };

    /// <summary>
    /// 构建过滤表达式（ctx=null 恒真）。只允许常量比较，满足 SqlSugar 翻译。
    /// ⚠️ 必须用「具体实体属性」而非 IDataScope 接口成员构造表达式：泛型约束 T : IDataScope 下
    /// `it => it.OwnerUserId` 会绑定到接口属性，SqlSugar 取不到实体列映射 → 拼成 owneruserid(缺下划线) → PG 42703。
    /// 故用 typeof(T).GetProperty(...) 显式指向实体属性。
    /// </summary>
    public static Expression<Func<T, bool>> Filter<T>(ScopeCtx? ctx) where T : IDataScope
    {
        if (ctx is null) return _ => true;

        var it = Expression.Parameter(typeof(T), "it");
        var owner = Expression.Property(it, typeof(T).GetProperty(nameof(IDataScope.OwnerUserId))!);
        var dept = Expression.Property(it, typeof(T).GetProperty(nameof(IDataScope.DeptId))!);

        var body = ctx.Best switch
        {
            // 无部门用户看不到「本部门/及以下/自定义」数据：用 -1 常量保证空集
            DataScopeType.Dept => EqNullable(dept, ctx.DeptId ?? -1),
            DataScopeType.DeptAndChild => InDeptIds(dept, ctx.DeptIds),
            DataScopeType.Custom => InDeptIds(dept, ctx.DeptIds),
            _ => EqNullable(owner, ctx.UserId)
        };
        return Expression.Lambda<Func<T, bool>>(body, it);
    }

    private static Expression EqNullable(MemberExpression prop, long value)
        => Expression.Equal(prop, Expression.Constant(value, typeof(long?)));

    private static Expression InDeptIds(MemberExpression deptProp, IReadOnlyList<long> ids)
    {
        var list = ids.ToList();
        var contains = typeof(List<long>).GetMethod(nameof(List<long>.Contains))!;
        var hasValue = Expression.Property(deptProp, "HasValue");
        var value = Expression.Property(deptProp, "Value");
        var call = Expression.Call(Expression.Constant(list, typeof(List<long>)), contains, value);
        return Expression.AndAlso(hasValue, call);
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
