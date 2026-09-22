using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>部门树</summary>
[SugarTable("sys_dept")]
public class SysDept : BaseEntity
{
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? ParentId { get; set; }

    /// <summary>部门编码（唯一，迁移脚本建过滤唯一索引）</summary>
    [SugarColumn(Length = 64)]
    public string DeptCode { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string DeptName { get; set; } = "";

    /// <summary>负责人姓名（展示用）</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Leader { get; set; }

    /// <summary>负责人用户 Id（审批流「部门主管」解析用）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? LeaderUserId { get; set; }

    public int Sort { get; set; }

    public int Status { get; set; }

    /// <summary>祖级链（逗号分隔 Id，如 0,1,3；「本部门及以下」查询用）</summary>
    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Ancestors { get; set; }
}

/// <summary>岗位（审批权限载体：审批人可按岗位解析）</summary>
[SugarTable("sys_position")]
public class SysPosition : BaseEntity
{
    /// <summary>岗位编码（唯一，迁移脚本建过滤唯一索引）</summary>
    [SugarColumn(Length = 64)]
    public string PositionCode { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string PositionName { get; set; } = "";

    public int Sort { get; set; }

    public int Status { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}
