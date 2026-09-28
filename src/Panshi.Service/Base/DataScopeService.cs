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

        // ⚠️ 已知缺陷（行为保持不变地记在这里，改动需单独评审）：多角色「取最宽档」用的是枚举数值序
        // All=1 < Dept=2 < DeptAndChild=3 < Self=4 < Custom=5，但真实宽度是 Dept ⊂ DeptAndChild，
        // 且 Custom 的宽度取决于授权了哪些部门、根本不可比。所以同时持有「本部门」+「本部门及以下」
        // 两个角色时会被错误收窄成 Dept；Custom 与 Self 并存时 Custom 会被整个忽略。
        // 正确语义是按角色并集（多个谓词 OR），而非挑一个最宽的。修它会放宽可见范围，属安全面变更。
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
            .Where(u => IsVisible(ctx, u))
            .Select(u => u.UserName)
            .Distinct()
            .ToList();
    }

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

        var plan = PlanOf(ctx.Best);
        var body = plan switch
        {
            // 无部门用户看不到「本部门/及以下/自定义」数据：用 -1 常量保证空集
            ScopePlan.OwnerOnly => EqNullable(owner, ctx.UserId),
            ScopePlan.DeptEq => EqNullable(dept, ctx.DeptId ?? -1),
            ScopePlan.DeptIn => InDeptIds(dept, ctx.DeptIds),
            _ => throw new InvalidOperationException($"未登记的数据权限形态 {plan}")
        };
        return Expression.Lambda<Func<T, bool>>(body, it);
    }

    /// <summary>
    /// 单条实体是否落在 ctx 可见范围内——与 Filter&lt;T&gt; 共用 PlanOf 这一份语义的内存版，
    /// 供「按 id 直读详情」这类无法用查询表达式收敛的入口做归属校验（读必须有，否则列表过滤等于没做）。
    /// </summary>
    public static bool IsVisible<T>(ScopeCtx? ctx, T entity) where T : IDataScope
    {
        if (ctx is null) return true;
        var plan = PlanOf(ctx.Best);
        return plan switch
        {
            ScopePlan.OwnerOnly => entity.OwnerUserId == ctx.UserId,
            ScopePlan.DeptEq => entity.DeptId == (ctx.DeptId ?? -1),
            ScopePlan.DeptIn => entity.DeptId is long d && ctx.DeptIds.Contains(d),
            _ => throw new InvalidOperationException($"未登记的数据权限形态 {plan}")
        };
    }

    /// <summary>
    /// 五档 → 三种行谓词形态。这是全站唯一一份数据权限判定语义（表达式版与内存版都从这里派生），
    /// 新增档位必须在这里登记，否则运行期直接抛出——安全谓词宁可乐观地炸，也不静默降级成「只看本人」。
    /// </summary>
    private static ScopePlan PlanOf(DataScopeType scope) => scope switch
    {
        DataScopeType.Self => ScopePlan.OwnerOnly,
        DataScopeType.Dept => ScopePlan.DeptEq,
        DataScopeType.DeptAndChild => ScopePlan.DeptIn,
        DataScopeType.Custom => ScopePlan.DeptIn,
        DataScopeType.All => throw new InvalidOperationException("All 档不应出现在 ScopeCtx：ResolveAsync 已提前返回 null"),
        _ => throw new NotSupportedException($"未登记的数据权限档位 {scope}（({(int)scope})）")
    };

    private enum ScopePlan
    {
        OwnerOnly,
        DeptEq,
        DeptIn
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
