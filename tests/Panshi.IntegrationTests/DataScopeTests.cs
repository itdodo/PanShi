using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 数据权限过滤回归：非超管查询必须落到 snake_case 列 owner_user_id / dept_id。
/// 曾因 DataScopeService.Filter&lt;T&gt; 的泛型 lambda 绑定到 IDataScope 接口成员，
/// SqlSugar 取不到实体列映射 → 拼成 owneruserid → PG 42703（报销/采购列表对普通用户 500）。
/// </summary>
[Collection("pg")]
public class DataScopeTests(PgFixture fx) : PgTestBase(fx)
{
    [Theory]
    [InlineData(DataScopeType.Self)]
    [InlineData(DataScopeType.Dept)]
    [InlineData(DataScopeType.DeptAndChild)]
    [InlineData(DataScopeType.Custom)]
    public async Task Filter_Hits_SnakeColumns_And_Filters_Correctly(DataScopeType scope)
    {
        using var _ = As(1, "admin", 2);
        var owner = 930_000_001L;
        var dept = 930_000_002L;

        var repo = Fx.Repo<BizExpense>();
        var doc = await repo.InsertAsync(new BizExpense
        {
            DocNo = "BX_DS_" + SnowflakeId.NextId(), OwnerUserId = owner, OwnerUserName = "ds_owner",
            DeptId = dept, Amount = 10m, Reason = "数据权限回归", Status = BizDocStatus.Draft
        });
        try
        {
            var ctx = new ScopeCtx(owner, dept, scope, [dept]);
            var mine = await Db.Queryable<BizExpense>().Where(DataScopeService.Filter<BizExpense>(ctx)).ToListAsync();
            Assert.Contains(mine, x => x.Id == doc.Id); // 命中自己的数据 → 证明列名正确、未抛 42703

            var other = new ScopeCtx(owner + 1, dept + 1, scope, [dept + 1]);
            var notMine = await Db.Queryable<BizExpense>().Where(DataScopeService.Filter<BizExpense>(other)).ToListAsync();
            Assert.DoesNotContain(notMine, x => x.Id == doc.Id); // 归属人/部门不同 → 被过滤掉
        }
        finally
        {
            await Db.Deleteable<BizExpense>().Where(x => x.Id == doc.Id).ExecuteCommandAsync();
        }
    }

    /// <summary>
    /// 表达式版与内存版必须逐行同判——收敛成一份 PlanOf 语义之后，这条用例负责钉住这一点。
    /// actorHasDept=false 是必需的一维：「无部门用户 → 空集」靠 SQL 侧的 -1 哨兵实现，
    /// 若内存版漏掉哨兵就会把 dept_id 为 null 的行判成可见，而两版都带部门时根本看不出差异。
    /// </summary>
    [Theory]
    [InlineData(DataScopeType.Self, true)]
    [InlineData(DataScopeType.Self, false)]
    [InlineData(DataScopeType.Dept, true)]
    [InlineData(DataScopeType.Dept, false)]
    [InlineData(DataScopeType.DeptAndChild, true)]
    [InlineData(DataScopeType.DeptAndChild, false)]
    [InlineData(DataScopeType.Custom, true)]
    [InlineData(DataScopeType.Custom, false)]
    public async Task Filter_And_IsVisible_Never_Diverge(DataScopeType scope, bool actorHasDept)
    {
        using var _ = As(1, "admin", 2);
        var tag = SnowflakeId.NextId();
        var owner = 940_000_000L + tag % 100_000L;
        var deptA = 941_000_000L + tag % 100_000L;
        var deptB = deptA + 1;

        var rows = new List<BizExpense>();
        for (var i = 0; i < 4; i++)
        {
            rows.Add(new BizExpense
            {
                DocNo = $"BX_DV_{tag}_{i}", OwnerUserId = i % 2 == 0 ? owner : owner + 7,
                OwnerUserName = "dv", DeptId = i switch { 0 => deptA, 1 => deptB, 2 => deptA, _ => null },
                Amount = 1m, Reason = "表达式/内存同判回归", Status = BizDocStatus.Draft
            });
        }

        await Db.Insertable(rows).ExecuteCommandAsync();
        var ids = rows.Select(r => r.Id).ToList();
        try
        {
            var ctx = new ScopeCtx(owner, actorHasDept ? deptA : null, scope, [deptA, deptB]);

            var sqlIds = (await Db.Queryable<BizExpense>().Where(DataScopeService.Filter<BizExpense>(ctx))
                    .ToListAsync())
                .Where(x => ids.Contains(x.Id)).Select(x => x.Id).OrderBy(x => x).ToList();
            var memIds = rows.Where(r => DataScopeService.IsVisible(ctx, r)).Select(r => r.Id).OrderBy(x => x).ToList();

            Assert.Equal(sqlIds, memIds);
            // -1 哨兵的正向断言：无部门 actor 在 Dept 档必须什么都看不到
            if (!actorHasDept && scope == DataScopeType.Dept)
            {
                Assert.Empty(sqlIds);
                Assert.Empty(memIds);
            }
        }
        finally
        {
            await Db.Deleteable<BizExpense>().In(ids).ExecuteCommandAsync();
        }
    }

    /// <summary>All 档不该出现在 ctx（ResolveAsync 提前返回 null）；真出现时要抛出而不是静默按某一档过滤。</summary>
    [Fact]
    public void Filter_With_All_Scope_Throws_Instead_Of_Silently_Filtering()
        => Assert.Throws<InvalidOperationException>(
            () => DataScopeService.Filter<BizExpense>(new ScopeCtx(1, 1, DataScopeType.All, [])));

    /// <summary>
    /// 用户列表的数据权限必须下推 SQL。修复前是「取回一页再内存过滤」：
    /// total 保持库侧值（真机实测 rows=[] 而 total=4），且受限账号会翻出整页空白。
    /// </summary>
    [Fact]
    public async Task User_Page_Pushes_Scope_Into_Sql_Keeping_Total_And_Pages_Truthful()
    {
        var tag = SnowflakeId.NextId();
        var deptA = 942_000_000L + tag % 100_000L;
        var deptB = deptA + 1;

        var role = new SysRole
        {
            Id = SnowflakeId.NextId(), RoleCode = $"r_{tag}", RoleName = "用户分页夹具角色",
            DataScope = DataScopeType.Dept, Status = EnableStatus.Enabled, Sort = 999
        };
        SysUser User(string kind, int seq, long dept)
        {
            var u = new SysUser
            {
                Id = SnowflakeId.NextId(), UserName = $"{kind}{seq}_{tag}", NickName = "分页探针",
                Password = "not-used", DeptId = dept, Status = EnableStatus.Enabled, PwdUpdateTime = DateTime.Now
            };
            u.OwnerUserId = u.Id;
            return u;
        }

        var mine = new[] { User("a", 1, deptA), User("a", 2, deptA), User("a", 3, deptA) };
        var others = new[] { User("b", 1, deptB), User("b", 2, deptB) };
        var all = mine.Concat(others).ToList();
        await Db.Insertable(role).ExecuteCommandAsync();
        await Db.Insertable(all).ExecuteCommandAsync();
        await Db.Insertable(new SysUserRole
        {
            Id = SnowflakeId.NextId(), UserId = mine[0].Id, RoleId = role.Id
        }).ExecuteCommandAsync();

        try
        {
            var users = Fx.UserService();
            var p1 = await users.PageAsync(new UserQuery { PageNum = 1, PageSize = 2 }, mine[0].Id);
            var p2 = await users.PageAsync(new UserQuery { PageNum = 2, PageSize = 2 }, mine[0].Id);

            Assert.Equal(3, p1.Total); // total 只数本部门 3 人，不再把 deptB 算进来
            Assert.Equal(2, p1.Rows.Count); // 第一页必须是满页——后置过滤时这里可能是 0
            var seen = p1.Rows.Concat(p2.Rows).Select(r => r.UserName).ToList();
            Assert.Equal(3, seen.Distinct().Count());
            Assert.All(seen, n => Assert.StartsWith("a", n));
        }
        finally
        {
            await Db.Deleteable<SysUser>().In(all.Select(u => u.Id).ToList()).ExecuteCommandAsync();
            await Db.Deleteable<SysUserRole>().Where(l => l.UserId == mine[0].Id).ExecuteCommandAsync();
            await Db.Deleteable<SysRole>().Where(r => r.Id == role.Id).ExecuteCommandAsync();
        }
    }
}
