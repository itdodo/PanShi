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
}
