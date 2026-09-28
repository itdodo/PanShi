using System.Linq.Expressions;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;

namespace Panshi.Service.Base;

/// <summary>
/// 数据权限上下文 = **多角色可见集的并集**，预解析成常量（查询表达式内零方法调用——红线 #1）。
/// 并集只有两种形态，所以这里不存「档位」，只存展开后的结果：
/// IncludeSelf（任一角色为「仅本人」）+ DeptIds（各角色「本部门/及以下/自定义」授权部门的并集）。
/// null = 不过滤（超管，或任一角色为 All）。
/// </summary>
public sealed record ScopeCtx(long UserId, bool IncludeSelf, IReadOnlyList<long> DeptIds);

/// <summary>
/// 数据权限五档（蓝图§5.4）：All 全部 / Dept 本部门 / DeptAndChild 本部门及以下 / Self 仅本人 / Custom 自定义。
/// 列表侧用法：Expressionable.Create&lt;T&gt;() 攒完其它条件后 exp.And(DataScopeService.Filter&lt;T&gt;(ctx))
/// ——必须下推 SQL，别学「取回一页再内存过滤」（会让 total 失真、还会翻出空页，见 UserService 的教训）。
/// 详情侧用法：IsVisible(ctx, entity) 做归属校验。实体实现 IDataScope 即接管。
/// </summary>
public class DataScopeService(
    IRepository<SysUser> userRepo,
    IRepository<SysUserRole> userRoleRepo,
    IRepository<SysRole> roleRepo,
    IRepository<SysRoleDept> roleDeptRepo,
    IRepository<SysDept> deptRepo)
{
    /// <summary>
    /// 解析当前用户的数据权限上下文。多角色是**可见集取并**：任一角色为 All 即不过滤（返回 null）；
    /// 其余角色各自展开成「看自己」或「一批部门」，最后并起来。
    /// ⚠️ 别退回「按枚举数值取最宽档」：DataScopeType 的数值序不代表宽度——Dept(2) ⊂ DeptAndChild(3)，
    /// Custom 更是取决于授权了哪些部门、根本不可比。那样 {本部门}+{本部门及以下} 会被错误收窄成 Dept、
    /// {自定义}+{仅本人} 会让 Custom 整个失效（本方法在 2026-09-28 之前就是这么写的）。
    /// </summary>
    public async Task<ScopeCtx?> ResolveAsync(long userId)
    {
        var user = await userRepo.FindAsync(userId);
        if (user is null) return null;

        var links = await userRoleRepo.ListAsync(l => l.UserId == userId);
        var roleIds = links.Select(l => l.RoleId).ToHashSet();
        var roles = (await roleRepo.ListAsync())
            .Where(r => roleIds.Contains(r.Id) && r.Status == EnableStatus.Enabled).ToList();

        // 无角色 = 只看自己（沿用既有口径：不是看全部，也不是看空）
        if (roles.Count == 0) return new ScopeCtx(userId, true, []);
        if (roles.Any(r => r.DataScope == DataScopeType.All)) return null;

        var includeSelf = false;
        var deptIds = new List<long>();
        var customRoleIds = new List<long>();
        List<SysDept>? allDepts = null; // 只在真有「本部门及以下」角色时才整表取一次，避免每角色一趟查询

        foreach (var role in roles)
        {
            switch (role.DataScope)
            {
                case DataScopeType.Self:
                    includeSelf = true;
                    break;
                case DataScopeType.Dept:
                    if (user.DeptId is long own) deptIds.Add(own);
                    break;
                case DataScopeType.DeptAndChild:
                    if (user.DeptId is long root)
                    {
                        allDepts ??= await deptRepo.ListAsync();
                        deptIds.Add(root);
                        deptIds.AddRange(DescendantIds(allDepts, root));
                    }

                    break;
                case DataScopeType.Custom:
                    customRoleIds.Add(role.Id);
                    break;
                case DataScopeType.All:
                    break; // 上面已提前返回，这里只是让 switch 覆盖全部已知档位
                default:
                    throw new NotSupportedException(
                        $"未处理的数据权限档位 {role.DataScope}（{(int)role.DataScope}）：新增档位必须在本方法登记它的可见集来源");
            }
        }

        if (customRoleIds.Count > 0)
        {
            var custom = customRoleIds.ToHashSet();
            deptIds.AddRange((await roleDeptRepo.ListAsync(rd => custom.Contains(rd.RoleId))).Select(rd => rd.DeptId));
        }

        return new ScopeCtx(userId, includeSelf, deptIds.Distinct().ToList());
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

        // 并集最多一个 OR。两者皆空时用 -1 常量保证恒假（无部门却只拿到「本部门」类角色 → 什么都看不到），
        // 与内存版 IsVisible 同判：真库里不存在 dept_id = -1 的行，NULL 也匹配不上。
        var parts = new List<Expression>();
        if (ctx.IncludeSelf) parts.Add(EqNullable(owner, ctx.UserId));
        if (ctx.DeptIds.Count > 0) parts.Add(InDeptIds(dept, ctx.DeptIds));

        var body = parts.Count == 0 ? EqNullable(dept, -1) : parts.Aggregate(Expression.OrElse);
        return Expression.Lambda<Func<T, bool>>(body, it);
    }

    /// <summary>
    /// 单条实体是否落在 ctx 可见范围内——与 Filter&lt;T&gt; 同一套并集语义的内存版，
    /// 供「按 id 直读详情」这类无法用查询表达式收敛的入口做归属校验（读必须有，否则列表过滤等于没做）。
    /// </summary>
    public static bool IsVisible<T>(ScopeCtx? ctx, T entity) where T : IDataScope
    {
        if (ctx is null) return true;
        if (ctx.IncludeSelf && entity.OwnerUserId == ctx.UserId) return true;
        return entity.DeptId is long d && ctx.DeptIds.Contains(d);
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

    /// <summary>
    /// 子孙部门（不含自身；内存 BFS，Except(result) 顺带兜住脏数据里的父子环）。
    /// 收非泛型 IReadOnlyList 是为了让 ResolveAsync 只整表取一次部门。
    /// </summary>
    private static List<long> DescendantIds(IReadOnlyList<SysDept> all, long root)
    {
        var result = new List<long>();
        var frontier = new List<long> { root };
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
