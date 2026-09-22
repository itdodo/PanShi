using Panshi.Model.Entities;
using SqlSugar;

namespace Panshi.Repository;

/// <summary>
/// 启动初始化：等待数据库就绪 → 逐表 CodeFirst（红线 #17：历史崩溃可能留下半残表元数据，逐表定位）。
/// 数据回填/索引调优类 DDL 走 db/migrations/NNNN_描述.sql（见 DbMigrationRunner）。
/// </summary>
public static class DbInitializer
{
    public static IReadOnlyList<Type> EntityTypes { get; } = typeof(BaseEntity).Assembly
        .GetTypes()
        .Where(t => !t.IsAbstract && t.IsClass && typeof(BaseEntity).IsAssignableFrom(t))
        .OrderBy(t => t.Name)
        .ToArray();

    /// <summary>阻塞式建表（幂等，SqlSugar PG CodeFirst 内部处理存在性判断）。</summary>
    public static void InitializeTables(ISqlSugarClient db, Action<string>? log = null)
    {
        foreach (var type in EntityTypes)
        {
            var tableName = db.EntityMaintenance.GetTableName(type.Name);
            log?.Invoke($"CodeFirst {tableName} ({type.Name})");
            try
            {
                db.CodeFirst.InitTables(type);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"表 {tableName}（{type.Name}）CodeFirst 失败——若是历史崩溃导致的半残元数据，需先 DROP TABLE {tableName} 再启动重建", ex);
            }
        }
    }

    /// <summary>带重试的连接等待（docker compose 场景数据库可能慢于应用就绪）。</summary>
    public static async Task WaitForDatabaseAsync(ISqlSugarClient db, int maxAttempts = 24, int delayMs = 5000,
        CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                db.Ado.CommandTimeOut = 5;
                db.Ado.GetScalar("select 1");
                return;
            }
            catch when (attempt < maxAttempts)
            {
                await Task.Delay(delayMs, ct);
            }
        }
    }
}
