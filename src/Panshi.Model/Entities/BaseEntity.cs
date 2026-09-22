using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>
/// 全表基类：雪花主键 + 审计字段 + 软删除 + 乐观锁。
/// ⚠️ 分页默认排序必须用 CreateTime（雪花 Id 仅在生成器位宽配置一致时与时间同序）。
/// Version 乐观锁由 UpdateWithVersionCheckAsync 单条 SQL 手工维护，
/// 禁止使用 Updateable.WhereColumns(版本列)（会以版本列替换主键条件，造成数据损坏）。
/// </summary>
public abstract class BaseEntity
{
    /// <summary>雪花 Id（JSON 序列化为字符串）</summary>
    [SugarColumn(IsPrimaryKey = true, ColumnDataType = "bigint")]
    public long Id { get; set; }

    /// <summary>创建时间（CodeFirst 映射 timestamp）</summary>
    [SugarColumn(ColumnDataType = "timestamp")]
    public DateTime CreateTime { get; set; }

    /// <summary>创建人</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? CreateBy { get; set; }

    /// <summary>更新时间</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "timestamp")]
    public DateTime? UpdateTime { get; set; }

    /// <summary>更新人</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? UpdateBy { get; set; }

    /// <summary>软删除标记（全局过滤器默认过滤已删除行）</summary>
    public bool IsDeleted { get; set; }

    /// <summary>乐观锁版本号（新增=0，每次更新 +1）</summary>
    public int Version { get; set; }
}
