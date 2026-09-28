using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 数据权限过滤回归。两层：
/// ① 表达式必须落到 snake_case 列 owner_user_id / dept_id——曾因泛型 lambda 绑定到 IDataScope 接口成员，
///    SqlSugar 取不到实体列映射 → 拼成 owneruserid → PG 42703（报销/采购列表对普通用户 500）。
/// ② 多角色是**可见集取并**，不是「按枚举数值挑最宽的一档」。
/// </summary>
[Collection("pg")]
public class DataScopeTests(PgFixture fx) : PgTestBase(fx)
{
    private static string Tag() => "ds" + SnowflakeId.NextId();

    private long RootDept => 1; // 种子：总公司，2/3/4/5 都是它的直接子部门

    private async Task<SysUser> UserAsync(string tag, long? dept)
    {
        var u = new SysUser
        {
            Id = SnowflakeId.NextId(), UserName = $"u_{tag}", NickName = "数据权限夹具",
            Password = "not-used", DeptId = dept, Status = EnableStatus.Enabled, PwdUpdateTime = DateTime.Now
        };
        u.OwnerUserId = u.Id;
        await Db.Insertable(u).ExecuteCommandAsync();
        return u;
    }

    /// <summary>建一个角色并挂到用户上；scope=Custom 时同时授权 customDept。</summary>
    private async Task<SysRole> RoleAsync(string tag, DataScopeType scope, long? customDept = null)
    {
        var r = new SysRole
        {
            Id = SnowflakeId.NextId(), RoleCode = $"r_{tag}_{(int)scope}", RoleName = "数据权限夹具角色",
            DataScope = scope, Status = EnableStatus.Enabled, Sort = 999
        };
        await Db.Insertable(r).ExecuteCommandAsync();
        if (customDept is long cd)
            await Db.Insertable(new SysRoleDept { Id = SnowflakeId.NextId(), RoleId = r.Id, DeptId = cd })
                .ExecuteCommandAsync();
        return r;
    }

    private async Task LinkAsync(long userId, SysRole role)
        => await Db.Insertable(new SysUserRole { Id = SnowflakeId.NextId(), UserId = userId, RoleId = role.Id })
            .ExecuteCommandAsync();

    private async Task<BizExpense> DocAsync(string tag, long owner, long? dept)
    {
        var doc = new BizExpense
        {
            DocNo = $"BX_{tag}", OwnerUserId = owner, OwnerUserName = "ds_owner", DeptId = dept,
            Amount = 10m, Reason = "数据权限回归", Status = BizDocStatus.Draft
        };
        await Db.Insertable(doc).ExecuteCommandAsync();
        return doc;
    }

    private async Task CleanAsync(string tag, IEnumerable<SysRole> roles, params SysUser[] users)
    {
        await Db.Deleteable<BizExpense>().Where(x => x.Reason == "数据权限回归" && x.DocNo!.Contains(tag))
            .ExecuteCommandAsync();
        foreach (var r in roles)
        {
            await Db.Deleteable<SysRoleDept>().Where(x => x.RoleId == r.Id).ExecuteCommandAsync();
            await Db.Deleteable<SysUserRole>().Where(x => x.RoleId == r.Id).ExecuteCommandAsync();
            await Db.Deleteable<SysRole>().Where(x => x.Id == r.Id).ExecuteCommandAsync();
        }

        foreach (var u in users)
            await Db.Deleteable<SysUser>().Where(x => x.Id == u.Id).ExecuteCommandAsync();
    }

    private async Task<List<long>> VisibleIdsAsync(ScopeCtx? ctx)
        => (await Db.Queryable<BizExpense>().Where(DataScopeService.Filter<BizExpense>(ctx)).ToListAsync())
            .Where(x => x.Reason == "数据权限回归").Select(x => x.Id).ToList();

    // ---------- ① 列名映射 + 单档基本判定 ----------

    /// <summary>
    /// 三种「应当命中」的 ctx 形态都要能落到自己的数据（证明列名正确、未抛 42703），换个 owner/dept 就必须看不到。
    /// 第四种形态（既不看自己、又没有授权部门）恒假，由 Filter_And_IsVisible_Never_Diverge 里的 Assert.Empty 那条管。
    /// </summary>
    [Theory]
    [InlineData(true, 0)] // 只看自己
    [InlineData(false, 1)] // 只给一批部门
    [InlineData(true, 1)] // 两者都要（并集）
    public async Task Filter_Hits_SnakeColumns_And_Filters_Correctly(bool includeSelf, int deptCount)
    {
        using var _ = As(1, "admin", 2);
        var tag = Tag();
        var owner = 930_000_001L;
        var dept = 930_000_002L;
        var doc = await DocAsync(tag, owner, dept);
        try
        {
            var mine = new ScopeCtx(owner, includeSelf, deptCount == 0 ? [] : [dept]);
            Assert.Contains(doc.Id, await VisibleIdsAsync(mine));

            var other = new ScopeCtx(owner + 1, includeSelf, deptCount == 0 ? [] : [dept + 1]);
            Assert.DoesNotContain(doc.Id, await VisibleIdsAsync(other));
        }
        finally
        {
            await Db.Deleteable<BizExpense>().Where(x => x.Id == doc.Id).ExecuteCommandAsync();
        }
    }

    // ---------- ② 表达式版与内存版必须逐行同判 ----------

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public async Task Filter_And_IsVisible_Never_Diverge(bool includeSelf, int deptCount)
    {
        using var _ = As(1, "admin", 2);
        var tag = Tag();
        var owner = 940_000_001L;
        var deptA = 941_000_001L;
        var deptB = 941_000_002L;

        var rows = new List<BizExpense>();
        for (var i = 0; i < 4; i++)
        {
            rows.Add(new BizExpense
            {
                DocNo = $"BX_DV_{tag}_{i}", OwnerUserId = i % 2 == 0 ? owner : owner + 7,
                OwnerUserName = "dv", DeptId = i switch { 0 => deptA, 1 => deptB, 2 => deptA, _ => null },
                Amount = 1m, Reason = "数据权限回归", Status = BizDocStatus.Draft
            });
        }

        await Db.Insertable(rows).ExecuteCommandAsync();
        var ids = rows.Select(r => r.Id).ToList();
        try
        {
            var ctx = new ScopeCtx(owner, includeSelf, deptCount == 0 ? [] : [deptA, deptB]);
            var sqlIds = (await VisibleIdsAsync(ctx)).OrderBy(x => x).ToList();
            var memIds = rows.Where(r => DataScopeService.IsVisible(ctx, r)).Select(r => r.Id).OrderBy(x => x).ToList();
            Assert.Equal(sqlIds, memIds);
            if (!includeSelf && deptCount == 0) Assert.Empty(sqlIds); // 恒假那一支的正向断言
        }
        finally
        {
            await Db.Deleteable<BizExpense>().In(ids).ExecuteCommandAsync();
        }
    }

    // ---------- ③ 多角色取并（本批修正的就是这三条） ----------

    /// <summary>
    /// {本部门} + {本部门及以下}：并集应覆盖子孙部门。
    /// 修正前按枚举数值序挑档，Dept(2) 比 DeptAndChild(3) 「更宽」，于是子孙部门整个看不到。
    /// </summary>
    [Fact]
    public async Task MultiRole_Dept_Plus_DeptAndChild_Sees_Descendants()
    {
        using var _ = As(1, "admin", 2);
        var tag = Tag();
        var actor = await UserAsync(tag, RootDept);
        var roles = new[]
        {
            await RoleAsync(tag, DataScopeType.Dept), await RoleAsync(tag, DataScopeType.DeptAndChild)
        };
        foreach (var r in roles) await LinkAsync(actor.Id, r);
        var childDoc = await DocAsync(tag + "child", 999_001L, 3); // 财务部：别人的、在子孙部门里的单
        try
        {
            var ctx = await Fx.DataScope().ResolveAsync(actor.Id);
            Assert.NotNull(ctx);
            Assert.Contains(3, ctx!.DeptIds); // 部门并集里必须有子孙部门
            Assert.DoesNotContain(childDoc.Id, await VisibleIdsAsync(new ScopeCtx(actor.Id, false, [RootDept])));
            Assert.Contains(childDoc.Id, await VisibleIdsAsync(ctx));
        }
        finally
        {
            await CleanAsync(tag, roles, actor);
            await Db.Deleteable<BizExpense>().Where(x => x.Id == childDoc.Id).ExecuteCommandAsync();
        }
    }

    /// <summary>
    /// {仅本人} + {自定义→某部门}：两条都得生效。
    /// 修正前 Custom(5) 数值最大 = 「最窄」，与 Self(4) 并存时 Custom 被整个忽略。
    /// </summary>
    [Fact]
    public async Task MultiRole_Self_Plus_Custom_Sees_Own_And_Custom_But_Not_Whole_Dept()
    {
        using var _ = As(1, "admin", 2);
        var tag = Tag();
        var customDept = 945_000_001L;
        var actor = await UserAsync(tag, RootDept);
        var selfRole = await RoleAsync(tag, DataScopeType.Self);
        var customRole = await RoleAsync(tag, DataScopeType.Custom, customDept);
        await LinkAsync(actor.Id, selfRole);
        await LinkAsync(actor.Id, customRole);

        var ownDoc = await DocAsync(tag + "own", actor.Id, 999_999L); // 自己的，部门故意设成无关值
        var customDoc = await DocAsync(tag + "cust", 999_002L, customDept); // 别人的，但在自定义授权部门
        var sameDeptDoc = await DocAsync(tag + "same", 999_003L, RootDept); // 别人的、同部门：不该看得到
        try
        {
            var ctx = await Fx.DataScope().ResolveAsync(actor.Id);
            Assert.NotNull(ctx);
            Assert.True(ctx!.IncludeSelf);
            Assert.Contains(customDept, ctx.DeptIds);

            var visible = await VisibleIdsAsync(ctx);
            Assert.Contains(ownDoc.Id, visible);
            Assert.Contains(customDoc.Id, visible);
            Assert.DoesNotContain(sameDeptDoc.Id, visible); // 并集没有顺手把「本部门」也放开
        }
        finally
        {
            await CleanAsync(tag, new[] { selfRole, customRole }, actor);
        }
    }

    /// <summary>任一角色是 All，就完全不过滤——哪怕另一个角色只有「仅本人」。</summary>
    [Fact]
    public async Task Any_All_Role_Disables_Filtering_Entirely()
    {
        using var _ = As(1, "admin", 2);
        var tag = Tag();
        var actor = await UserAsync(tag, RootDept);
        var roles = new[]
        {
            await RoleAsync(tag, DataScopeType.Self), await RoleAsync(tag, DataScopeType.All)
        };
        foreach (var r in roles) await LinkAsync(actor.Id, r);
        try
        {
            Assert.Null(await Fx.DataScope().ResolveAsync(actor.Id));
        }
        finally
        {
            await CleanAsync(tag, roles, actor);
        }
    }

    /// <summary>一个角色都没有 = 只看自己（不是看全部，也不是看空集）。</summary>
    [Fact]
    public async Task No_Role_Falls_Back_To_Self_Only()
    {
        using var _ = As(1, "admin", 2);
        var tag = Tag();
        var actor = await UserAsync(tag, RootDept);
        var ownDoc = await DocAsync(tag + "own", actor.Id, 999_998L);
        var otherDoc = await DocAsync(tag + "oth", 999_004L, RootDept);
        try
        {
            var ctx = await Fx.DataScope().ResolveAsync(actor.Id);
            Assert.NotNull(ctx);
            Assert.True(ctx!.IncludeSelf);
            Assert.Empty(ctx.DeptIds);
            var visible = await VisibleIdsAsync(ctx);
            Assert.Contains(ownDoc.Id, visible);
            Assert.DoesNotContain(otherDoc.Id, visible);
        }
        finally
        {
            await CleanAsync(tag, Array.Empty<SysRole>(), actor);
        }
    }
}
