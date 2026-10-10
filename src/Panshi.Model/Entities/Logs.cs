using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>操作日志（全局过滤器自动记录全部写操作；脱敏参数/耗时/IP/成败）</summary>
[SugarTable("sys_operation_log")]
public class SysOperationLog : BaseEntity
{
    [SugarColumn(Length = 256)]
    public string Module { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string Action { get; set; } = "";

    [SugarColumn(Length = 16)]
    public string Method { get; set; } = "";

    [SugarColumn(Length = 512)]
    public string Url { get; set; } = "";

    /// <summary>请求参数 JSON（password/secret/token/credential 词根脱敏后落库）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "text")]
    public string? Params { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? UserId { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? UserName { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Ip { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? UserAgent { get; set; }

    /// <summary>耗时毫秒</summary>
    public long ElapsedMs { get; set; }

    public bool Success { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "text")]
    public string? ErrorMsg { get; set; }
}

/// <summary>登录日志（成功/失败/锁定全记录）</summary>
[SugarTable("sys_login_log")]
public class SysLoginLog : BaseEntity
{
    [SugarColumn(Length = 64)]
    public string UserName { get; set; } = "";

    /// <summary>结果：登录成功/密码错误/账号锁定/验证码错误/账号停用…</summary>
    [SugarColumn(Length = 128)]
    public string Result { get; set; } = "";

    public bool Success { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Ip { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? UserAgent { get; set; }
}

/// <summary>字段级变更日志（UpdateWithAuditAsync：冲突不落审计、无差异不落审计）</summary>
[SugarTable("sys_change_log")]
public class SysChangeLog : BaseEntity
{
    [SugarColumn(Length = 64)]
    public string TableName { get; set; } = "";

    [SugarColumn(ColumnDataType = "bigint")]
    public long RecordId { get; set; }

    /// <summary>变更明细 JSON：[{field,label,before,after}]（敏感词根脱敏）</summary>
    [SugarColumn(ColumnDataType = "text")]
    public string Changes { get; set; } = "[]";

    [SugarColumn(ColumnDataType = "bigint")]
    public long UserId { get; set; }

    [SugarColumn(Length = 64)]
    public string UserName { get; set; } = "";
}

/// <summary>迁移记录（不继承 BaseEntity：Version 主键）</summary>
[SugarTable("sys_db_migration")]
public class SysDbMigration
{
    [SugarColumn(IsPrimaryKey = true, ColumnDataType = "int")]
    public int Version { get; set; }

    [SugarColumn(Length = 256)]
    public string Name { get; set; } = "";

    [SugarColumn(ColumnDataType = "timestamp")]
    public DateTime AppliedTime { get; set; }

    /// <summary>
    /// 脚本内容指纹（SHA-256 hex）。记账原本只认版本号，这一列让它同时认内容：
    /// 已应用的迁移被改动 → 执行器拒绝启动（见 DbMigrationRunner.Guard）。
    /// ⚠️ ColumnDataType 必须显式写 varchar：给已存在的表加 string 列时 SqlSugar 的 ALTER 路径会生成
    /// text(64)，PG 直接 42601（详见 docs/技术文档.md §三）。历史行是 NULL，由执行器首次遇到时回填。
    /// </summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "varchar(64)")]
    public string? Checksum { get; set; }
}
