using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Panshi.Model.Entities;
using SqlSugar;

namespace Panshi.Repository;

/// <summary>
/// 版本化迁移执行器（蓝图§四）：
/// 内嵌资源 db/migrations/NNNN_描述.sql；sys_db_migration 记录已应用版本 + 脚本内容指纹；幂等、⚠️ 禁改历史迁移。
/// PG 支持事务性 DDL，每个脚本单事务执行，失败即中止启动。
///
/// 两道以前没有的守卫：
/// ① 内容指纹（<see cref="Guard"/>）：记账只认版本号，「改一个已应用过的迁移」过去完全无声——
///    实测就栽过：0005 的两条索引在提交之前被测试库应用过（那一版还没这两行），此后追加进去永不重跑，
///    于是新库有、老库没有。现在指纹不符就拒绝启动，宁可起不来也不要两边各自长出不同的 schema。
/// ② advisory lock + 锁内复查：多实例同时启动时，光靠循环前读的那份 applied 快照会重复应用同一个版本
///    （`IF NOT EXISTS` 大多扛得住，但带数据写入的脚本会撞）。键取 `LockBase + version`，锁随事务结束自动释放。
/// </summary>
public static class DbMigrationRunner
{
    private const string Marker = ".db.migrations.";

    /// <summary>
    /// advisory lock 的键基（实际键 = 本值 + 版本号，于是「同一个版本」全局串行、不同版本互不挡）。
    /// 公开是为了用例能自己占住同一把键来证明排队行为，不是给业务代码用的。
    /// </summary>
    public const long LockBase = 740219_000;

    /// <summary>返回本次应用的脚本数。</summary>
    public static int Run(ISqlSugarClient db, Action<string>? log = null)
    {
        db.CodeFirst.InitTables<SysDbMigration>();

        var scripts = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.Contains(Marker, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(name =>
            {
                var file = name[(name.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..];
                var versionPart = file.Split('_', 2)[0];
                return (Resource: name, File: file, Version: int.Parse(versionPart), Sql: ReadResource(name));
            })
            .OrderBy(s => s.Version)
            .ToList();

        var count = 0;
        foreach (var script in scripts)
        {
            // 指纹要在应用前算：脚本里带 BOM/行尾差异都不该让同一份内容产生两个哈希
            var checksum = ChecksumOf(script.Sql);
            if (ApplyOne(db, script.Resource, script.File, script.Version, script.Sql, checksum, log)) count++;
            else Guard(db, script.File, script.Version, checksum, log);
        }

        WarnOrphans(db, scripts.Select(s => s.Version).ToHashSet(), log);
        return count;
    }

    /// <summary>脚本内容的稳定指纹：行尾归一 + 去尾部空白，SHA-256 hex（小写）。</summary>
    public static string ChecksumOf(string sql)
    {
        var normalized = sql.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    /// <summary>True=本次真的应用了；False=该版本已在流水里。</summary>
    private static bool ApplyOne(ISqlSugarClient db, string resource, string file, int version, string sql,
        string checksum, Action<string>? log)
    {
        db.Ado.Open();
        try
        {
            db.Ado.BeginTran();
            // 先拿这个版本的迁移锁，再在锁内确认流水——两个实例同时启动时后者会在这里等前者提交
            db.Ado.CommandTimeOut = 300;
            db.Ado.ExecuteCommand("select pg_advisory_xact_lock(@k)", new { k = LockBase + version });
            var recorded = db.Queryable<SysDbMigration>().Where(m => m.Version == version).Any();
            if (recorded)
            {
                db.Ado.CommitTran();
                return false;
            }

            db.Ado.ExecuteCommand(sql);
            db.Insertable(new SysDbMigration
            {
                Version = version,
                Name = file,
                AppliedTime = DateTime.Now,
                Checksum = checksum
            }).ExecuteCommand();
            db.Ado.CommitTran();
            log?.Invoke($"迁移已应用 {file}");
            return true;
        }
        catch
        {
            db.Ado.RollbackTran();
            throw;
        }
        finally
        {
            db.Ado.Close();
        }
    }

    /// <summary>
    /// 已应用的版本：指纹一致就放行；库里没指纹（加列之前应用的历史行）就把当前内容登记进去；
    /// 指纹不符 = 有人改了已经跑过的迁移，拒绝启动。
    /// </summary>
    private static void Guard(ISqlSugarClient db, string file, int version, string checksum, Action<string>? log)
    {
        var recorded = db.Queryable<SysDbMigration>().Where(m => m.Version == version).First();
        if (recorded is null) return; // 并发下刚被别的实例查过，交给下次启动

        if (string.IsNullOrEmpty(recorded.Checksum))
        {
            // 一次性豁免：指纹列是 0014 才加的，之前的行没有哈希。以「当前内容」为准登记，
            // 也就是默认历史库现在的结构就是这份脚本跑出来的——这正是 0005 那类坑无法追溯的地方，
            // 所以从今往后不符就硬拦，登记只发生一次。
            db.Ado.ExecuteCommand("update sys_db_migration set checksum=@c where version=@v",
                new { c = checksum, v = version });
            log?.Invoke($"迁移 {file} 首次登记内容指纹（历史行无哈希，按当前内容补记）");
            return;
        }

        if (recorded.Checksum == checksum) return;

        throw new InvalidOperationException(
            $"迁移 {file} 的内容与库里已应用的记录不一致（库里 {recorded.Checksum[..12]}…，当前 {checksum[..12]}…）。"
            + "⚠️ 已应用过的迁移不能改——记账只认版本号，改了它对这些库等于没改，还会让新库与老库的 schema 分叉。"
            + "正确做法是新起一个编号向前修（老库靠幂等语句收敛）；确属历史遗留需要校正时，"
            + "先确认该库真实结构，再 update sys_db_migration set checksum=新指纹 where version=…，然后把改动写进新的迁移。");
    }

    /// <summary>流水里有、脚本目录里没有——迁移文件被删了。只报不拦：结构已经在库里，拦启动换不来任何东西。</summary>
    private static void WarnOrphans(ISqlSugarClient db, HashSet<int> presentVersions, Action<string>? log)
    {
        var missing = db.Queryable<SysDbMigration>().ToList()
            .Where(m => !presentVersions.Contains(m.Version))
            .Select(m => $"{m.Version:0000}_{m.Name}")
            .ToList();
        if (missing.Count > 0)
        {
            log?.Invoke($"⚠️ 迁移流水有 {missing.Count} 条记录在脚本目录里找不到（文件被删过？）：{string.Join(", ", missing)}");
        }
    }

    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"迁移资源缺失：{name}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
