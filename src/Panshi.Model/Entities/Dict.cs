using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>字典类型（内置 10 组 sys_* 常用字典）</summary>
[SugarTable("sys_dict_type")]
public class SysDictType : BaseEntity
{
    [SugarColumn(Length = 64)]
    public string DictName { get; set; } = "";

    /// <summary>字典编码（唯一，迁移脚本建过滤唯一索引）</summary>
    [SugarColumn(Length = 64)]
    public string DictCode { get; set; } = "";

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}

/// <summary>字典项</summary>
[SugarTable("sys_dict_data")]
public class SysDictData : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long DictTypeId { get; set; }

    [SugarColumn(Length = 128)]
    public string Label { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string Value { get; set; } = "";

    public int Sort { get; set; }

    public int Status { get; set; }

    /// <summary>回显样式（info/success/warning/danger 语义）</summary>
    [SugarColumn(IsNullable = true, Length = 32)]
    public string? TagType { get; set; }

    /// <summary>是否默认</summary>
    public bool IsDefault { get; set; }
}

/// <summary>系统参数（内置：默认密码/锁定阈值/锁定时长/验证码开关/密码有效期/同账号互踢）</summary>
[SugarTable("sys_config")]
public class SysConfig : BaseEntity
{
    [SugarColumn(Length = 64)]
    public string ConfigName { get; set; } = "";

    /// <summary>参数键（唯一，如 sys.pwd.expireDays；迁移脚本建过滤唯一索引）</summary>
    [SugarColumn(Length = 128)]
    public string ConfigKey { get; set; } = "";

    [SugarColumn(IsNullable = true, Length = 1024)]
    public string? ConfigValue { get; set; }

    /// <summary>内置参数不可删</summary>
    public bool BuiltIn { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}
