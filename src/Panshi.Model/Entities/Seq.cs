using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>
/// 单据号发号器。一行一个「前缀+日期」号段，取号靠一条原子 UPDATE…RETURNING。
/// 取代原先的 COUNT(前缀)+1——那个写法两个并发请求会算出同一个号，插库直接撞单号唯一索引。
/// </summary>
[SugarTable("sys_doc_seq")]
public class SysDocSeq : BaseEntity
{
    /// <summary>号段键，如 RK20261002。唯一索引见迁移 0012（ON CONFLICT 依赖它）</summary>
    [SugarColumn(Length = 32)]
    public string SeqKey { get; set; } = "";

    /// <summary>已发出的最大序号</summary>
    public int SeqValue { get; set; }
}
