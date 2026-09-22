using System.Reflection;
using Panshi.Model.Entities;
using SqlSugar;

namespace Panshi.Repository;

/// <summary>
/// 版本化迁移执行器（蓝图§四）：
/// 内嵌资源 db/migrations/NNNN_描述.sql；sys_db_migration 记录已应用版本；幂等、⚠️ 禁改历史迁移。
/// PG 支持事务性 DDL，每个脚本单事务执行，失败即中止启动。
/// </summary>
public static class DbMigrationRunner
{
    private const string Marker = ".db.migrations.";

    /// <summary>返回本次应用的脚本数。</summary>
    public static int Run(ISqlSugarClient db, Action<string>? log = null)
    {
        db.CodeFirst.InitTables<SysDbMigration>();

        var applied = db.Queryable<SysDbMigration>().ToList().Select(m => m.Version).ToHashSet();

        var scripts = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(n => n.Contains(Marker, StringComparison.Ordinal) && n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(name =>
            {
                var file = name[(name.IndexOf(Marker, StringComparison.Ordinal) + Marker.Length)..];
                var versionPart = file.Split('_', 2)[0];
                return (Resource: name, File: file, Version: int.Parse(versionPart));
            })
            .OrderBy(s => s.Version)
            .ToList();

        var count = 0;
        foreach (var script in scripts)
        {
            if (applied.Contains(script.Version)) continue;

            var sql = ReadResource(script.Resource);
            db.Ado.Open();
            try
            {
                db.Ado.BeginTran();
                db.Ado.ExecuteCommand(sql);
                db.Insertable(new SysDbMigration
                {
                    Version = script.Version,
                    Name = script.File,
                    AppliedTime = DateTime.Now
                }).ExecuteCommand();
                db.Ado.CommitTran();
                log?.Invoke($"迁移已应用 {script.File}");
                count++;
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

        return count;
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
