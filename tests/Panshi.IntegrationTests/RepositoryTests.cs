using Panshi.Common.Exceptions;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>仓储语义：雪花（含批量）、软删/物删、乐观锁、连续编辑往返、字段审计、事务回滚、迁移幂等。</summary>
[Collection("pg")]
public class RepositoryTests(PgFixture fx) : PgTestBase(fx)
{
    [Fact]
    public async Task Single_Insert_Aop_Fills_Id_And_Audit_Fields()
    {
        using var _ = As(1, "admin", 2);
        var repo = Fx.Repo<SysPosition>();
        var p = await repo.InsertAsync(new SysPosition
        {
            PositionCode = "t_aop_" + SnowflakeId.NextId(), PositionName = "AOP测试", Sort = 1
        });
        Assert.True(p.Id > 1_000_000); // 雪花大数，非自增
        Assert.True(p.CreateTime > DateTime.Now.AddMinutes(-1));
        Assert.Equal(1, p.CreateBy);
    }

    [Fact]
    public async Task Batch_Insert_All_Rows_Get_NonZero_Unique_Ids()
    {
        // 红线 #8：批量直连不触发 AOP → InsertRangeAsync 显式填充
        var repo = Fx.Repo<SysPosition>();
        var tag = SnowflakeId.NextId();
        var rows = Enumerable.Range(0, 5)
            .Select(i => new SysPosition { PositionCode = $"t_batch_{tag}_{i}", PositionName = "批量" + i })
            .ToList();
        var n = await repo.InsertRangeAsync(rows);
        Assert.Equal(5, n);
        Assert.All(rows, r => Assert.True(r.Id != 0));
        Assert.Equal(5, rows.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public async Task Soft_Delete_Hides_Row_Physical_Delete_Removes()
    {
        var repo = Fx.Repo<SysPosition>();
        var code = "t_soft_" + SnowflakeId.NextId();
        var p = await repo.InsertAsync(new SysPosition { PositionCode = code, PositionName = "软删" });

        Assert.True(await repo.SoftDeleteAsync(p.Id));
        Assert.Null(await repo.FindAsync(p.Id)); // 全局过滤器生效
        Assert.NotNull(await Db.Queryable<SysPosition>().ClearFilter().InSingleAsync(p.Id)); // 仍在库里

        Assert.True(await repo.DeleteAsync(p.Id));
        Assert.Null(await Db.Queryable<SysPosition>().ClearFilter().InSingleAsync(p.Id));
    }

    [Fact]
    public async Task Version_Check_Fails_On_Stale_Value_And_Rolls_Forward()
    {
        var repo = Fx.Repo<SysPosition>();
        var p = await repo.InsertAsync(new SysPosition
        {
            PositionCode = "t_ver_" + SnowflakeId.NextId(), PositionName = "旧值", Sort = 1
        });
        Assert.Equal(0, p.Version);

        p.PositionName = "第一次";
        Assert.True(await repo.UpdateWithVersionCheckAsync(p, 0));
        Assert.Equal(1, p.Version);

        // 陈旧版本 → false（0 行）
        p.PositionName = "冲突";
        Assert.False(await repo.UpdateWithVersionCheckAsync(p, 0));
        var fresh = await repo.GetAsync(p.Id);
        Assert.Equal("第一次", fresh.PositionName);
        Assert.Equal(1, fresh.Version);
    }

    [Fact]
    public async Task Consecutive_Edits_Roundtrip_Through_Service_Entry()
    {
        // 回归铁律：连续编辑两次（红线 #6 的手工映射漏版本会在此暴露）
        var repo = Fx.Repo<SysPosition>();
        var p = await repo.InsertAsync(new SysPosition
        {
            PositionCode = "t_round_" + SnowflakeId.NextId(), PositionName = "v0"
        });

        for (var i = 1; i <= 3; i++)
        {
            var loaded = await repo.GetAsync(p.Id);
            loaded.PositionName = "v" + i;
            await repo.UpdateWithAuditAsync(loaded, loaded.Version); // 期望版本=当前版本
            p = loaded;
        }

        var final = await repo.GetAsync(p.Id);
        Assert.Equal("v3", final.PositionName);
        Assert.Equal(3, final.Version);
    }

    [Fact]
    public async Task UpdateWithAudit_Writes_ChangeLog_NoDiff_Writes_Nothing()
    {
        var repo = Fx.Repo<SysPosition>();
        var p = await repo.InsertAsync(new SysPosition
        {
            PositionCode = "t_audit_" + SnowflakeId.NextId(), PositionName = "orig", Remark = "keep"
        });
        var before = await Db.Queryable<SysChangeLog>().CountAsync(c => c.RecordId == p.Id);

        // 无差异 → 不落审计（版本仍会+1）
        await repo.UpdateWithAuditAsync(p, p.Version);
        Assert.Equal(before, await Db.Queryable<SysChangeLog>().CountAsync(c => c.RecordId == p.Id));

        p.PositionName = "changed";
        await repo.UpdateWithAuditAsync(p, p.Version);
        var log = await Db.Queryable<SysChangeLog>().FirstAsync(c => c.RecordId == p.Id);
        Assert.NotNull(log);
        Assert.Contains("ositionName", log.Changes);
        Assert.Contains("orig", log.Changes);
        Assert.Contains("changed", log.Changes);
    }

    [Fact]
    public async Task Conflict_Throws_409_And_No_Audit_Recorded()
    {
        var repo = Fx.Repo<SysPosition>();
        var p = await repo.InsertAsync(new SysPosition
        {
            PositionCode = "t_conf_" + SnowflakeId.NextId(), PositionName = "基线"
        });

        p.PositionName = "冲突写";
        await Assert.ThrowsAsync<BizException>(async () => await repo.UpdateWithAuditAsync(p, 999));
        Assert.False(await Db.Queryable<SysChangeLog>().AnyAsync(c => c.RecordId == p.Id));
    }

    [Fact]
    public async Task Tran_Rollback_On_Exception()
    {
        var repo = Fx.Repo<SysPosition>();
        var code = "t_tr_" + SnowflakeId.NextId();
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await Tran.RunAsync(Db, async () =>
            {
                await repo.InsertAsync(new SysPosition { PositionCode = code, PositionName = "回滚" });
                await Task.CompletedTask;
                throw new InvalidOperationException("模拟失败");
            }));
        Assert.False(await repo.ExistsAsync(p => p.PositionCode == code));
    }

    [Fact]
    public async Task Migration_Runner_Is_Idempotent()
    {
        var applied = DbMigrationRunner.Run(Db); // 已在 fixture 跑过 → 应为 0
        Assert.Equal(0, applied);
    }

    [Fact]
    public async Task Seed_Is_Idempotent_And_Admin_Exists()
    {
        DbSeeder.Seed(Db); // 二次执行不抛、不重复
        Assert.Equal(1, await Db.Queryable<SysUser>().CountAsync(u => u.UserName == "admin"));
        Assert.True(await Db.Queryable<SysMenu>().AnyAsync(m => m.Permission == "__seed_v1__"));
        // 红线 #18：可见菜单必须带图标
        var noIcon = await Db.Queryable<SysMenu>()
            .CountAsync(m => m.MenuType != MenuType.Button && (m.Icon == null || m.Icon == ""));
        Assert.Equal(0, noIcon);
    }

    [Fact]
    public async Task Sort_Injection_Blocked_By_Whitelist()
    {
        var q = new Panshi.Model.Dtos.PositionQuery
        {
            SortField = "position_code; drop table sys_user", SortOrder = "desc"
        };
        var (col, _) = q.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        Assert.Equal("create_time", col); // 白名单外回退
    }
}
