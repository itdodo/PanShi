using Panshi.Model.Entities;
using Panshi.Repository;
using SqlSugar;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 迁移指纹守卫与并发启动（迁移 0014 + DbMigrationRunner）。
/// 钉三件事：①已应用的迁移被改动 → 拒绝启动；②历史行「无指纹」只补记一次、不误拦；
/// ③多实例同时启动时同一个版本只被应用一次（advisory lock + 锁内复查）。
/// 探针都挂在 0003 上——它是纯菜单脚本，改它的指纹不碰任何结构。
/// </summary>
[Collection("pg")]
public class MigrationGuardTests(PgFixture fx) : PgTestBase(fx)
{
    private const int ProbeVersion = 3;

    private string? Recorded()
        => Db.Queryable<SysDbMigration>().Where(m => m.Version == ProbeVersion).First()?.Checksum;

    private void SetChecksum(int version, string? value)
        => Db.Ado.ExecuteCommand("update sys_db_migration set checksum=@c where version=@v", new { c = value, v = version });

    [Fact]
    public void Tampered_Applied_Migration_Blocks_Startup()
    {
        var original = Recorded();
        // 前提：fixture 引导时已经把历史行补记过，否则这条用例测不到「不符」那一支
        Assert.False(string.IsNullOrEmpty(original));

        try
        {
            SetChecksum(ProbeVersion, new string('f', 64));
            var ex = Record.Exception(() => DbMigrationRunner.Run(Db));

            Assert.IsType<InvalidOperationException>(ex);
            Assert.Contains("0003", ex!.Message);
            Assert.Contains("不能改", ex.Message);
        }
        finally
        {
            SetChecksum(ProbeVersion, original);
        }

        Assert.Equal(0, DbMigrationRunner.Run(Db)); // 还原后照常放行，且没有重复应用任何东西
    }

    [Fact]
    public void Null_Checksum_Is_Backfilled_Once_And_Does_Not_Block()
    {
        var original = Recorded();
        Assert.False(string.IsNullOrEmpty(original));

        SetChecksum(ProbeVersion, null);
        Assert.Equal(0, DbMigrationRunner.Run(Db)); // 没指纹的历史行：按当前内容登记，不拦
        Assert.Equal(original, Recorded()); // 登记的正是这份脚本的真实哈希

        SetChecksum(ProbeVersion, null);
        Assert.Equal(0, DbMigrationRunner.Run(Db)); // 第二次走「补记后相符」分支，结果一样
        Assert.Equal(original, Recorded());
    }

    /// <summary>
    /// 排队行为：另一个实例正在应用 0013 时，本实例必须等，而不是并行跑一遍。
    /// ⚠️ 这里刻意不用「N 个分支同时开跑」的写法——并发用例里那条是空跑：六个分支各自要先建连接、
    /// 跑 CodeFirst，真正进到「读流水→插入」时已经被错开上百毫秒，摘掉锁也不红。
    /// 换成自己占住同一把键，等待就成了确定事实。
    /// </summary>
    [Fact]
    public async Task Migration_Apply_Queues_Behind_The_Advisory_Lock()
    {
        Db.Ado.ExecuteCommand("delete from sys_db_migration where version=13"); // 0013 全是 IF NOT EXISTS，可安全重跑

        ISqlSugarClient holder = Fx.NewDb();
        holder.Ado.Open();
        holder.Ado.BeginTran();
        holder.Ado.ExecuteCommand("select pg_advisory_xact_lock(@k)",
            new { k = DbMigrationRunner.LockBase + 13 });

        try
        {
            var applying = Task.Run(() =>
            {
                ISqlSugarClient db = Fx.NewDb();
                return DbMigrationRunner.Run(db);
            });

            var won = await Task.WhenAny(applying, Task.Delay(2000)) == applying;
            Assert.False(won); // 锁被别人占着 → 不许并行应用

            holder.Ado.CommitTran(); // 放开锁
            Assert.Equal(1, await applying); // 放开后它才真的把 0013 应用掉
        }
        finally
        {
            if (holder.Ado.Transaction != null) holder.Ado.RollbackTran();
            holder.Ado.Close();
        }

        Assert.Equal(1, Db.Queryable<SysDbMigration>().Where(m => m.Version == 13).Count());
        Assert.False(string.IsNullOrEmpty(Db.Queryable<SysDbMigration>().Where(m => m.Version == 13).First()!.Checksum));
    }
}
