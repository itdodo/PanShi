using Panshi.Common.Runtime;
using SqlSugar;

namespace Panshi.Repository;

/// <summary>
/// 单据号发号器。号段按「前缀 + yyyyMMdd」一行，取号是一条原子 UPDATE…RETURNING，
/// 所以并发拿到的序号必然不同——原先的 COUNT(前缀)+1 在并发下会算出同一个号，插库撞唯一索引直接 500。
/// </summary>
public static class DocNumbers
{
    /// <summary>
    /// 取下一个单号。<paramref name="seedFrom"/> 只在该号段第一次被用到时调一次，
    /// 用存量单号数做起点，避免和历史数据（建号段表之前就存在的单子）撞车。
    /// </summary>
    public static async Task<string> NextAsync(ISqlSugarClient db, string prefix, Func<Task<long>> seedFrom)
    {
        var key = prefix + DateTime.Now.ToString("yyyyMMdd");

        var seeded = (await db.Ado.SqlQueryAsync<int>(
            "select count(*)::int from sys_doc_seq where seq_key = @key", new { key })).First();
        if (seeded == 0)
        {
            var seed = (int)await seedFrom();
            // 冲突目标 uk_sys_doc_seq_key 是部分唯一索引，ON CONFLICT 必须带同款索引谓词，否则 42P10
            await db.Ado.ExecuteCommandAsync(
                "insert into sys_doc_seq (id, seq_key, seq_value, create_time, is_deleted, version) "
                + "values (@id, @key, @seed, now(), false, 0) "
                + "on conflict (seq_key) where is_deleted = false do nothing",
                new { id = SnowflakeId.NextId(), key, seed });
        }

        var next = (await db.Ado.SqlQueryAsync<int>(
            "update sys_doc_seq set seq_value = seq_value + 1, update_time = now() "
            + "where seq_key = @key returning seq_value",
            new { key })).First();

        return $"{key}{next:D3}";
    }
}
