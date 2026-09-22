using Panshi.Model.Enums;
using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>用户</summary>
[SugarTable("sys_user")]
public class SysUser : BaseEntity, IDataScope
{
    /// <summary>登录名（唯一性由迁移脚本建过滤唯一索引 WHERE is_deleted=false）</summary>
    [SugarColumn(Length = 64)]
    public string UserName { get; set; } = "";

    /// <summary>密码（PBKDF2：iter$salt$hash，兼容历史 SHA256）</summary>
    [SugarColumn(Length = 256)]
    public string Password { get; set; } = "";

    /// <summary>昵称/姓名</summary>
    [SugarColumn(Length = 64)]
    public string NickName { get; set; } = "";

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? Phone { get; set; }

    [SugarColumn(IsNullable = true, Length = 128)]
    public string? Email { get; set; }

    /// <summary>头像（sys_file Id）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? AvatarFileId { get; set; }

    /// <summary>0正常 1停用</summary>
    public EnableStatus Status { get; set; }

    /// <summary>所属部门</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? DeptId { get; set; }

    /// <summary>密码最后修改时间（配合 sys.pwd.expireDays 强制改密）</summary>
    [SugarColumn(ColumnDataType = "timestamp")]
    public DateTime PwdUpdateTime { get; set; }

    /// <summary>是否强制下次登录改密（新建/重置/导入=true，用户改密后清除）</summary>
    [SugarColumn(DefaultValue = "false")]
    public bool MustChangePassword { get; set; }

    /// <summary>连续登录失败次数（防爆破）</summary>
    public int FailCount { get; set; }

    /// <summary>锁定截止时间（null/过期=未锁定）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "timestamp")]
    public DateTime? LockUntil { get; set; }

    /// <summary>数据权限归属人（配合 IDataScope；默认本人）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? OwnerUserId { get; set; }

    /// <summary>备注</summary>
    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }

    /// <summary>最后登录时间</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "timestamp")]
    public DateTime? LastLoginTime { get; set; }
}

/// <summary>用户↔角色</summary>
[SugarTable("sys_user_role")]
public class SysUserRole : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long UserId { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long RoleId { get; set; }
}

/// <summary>用户↔岗位（岗位=审批权限载体）</summary>
[SugarTable("sys_user_position")]
public class SysUserPosition : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long UserId { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long PositionId { get; set; }
}

/// <summary>在线会话（JWT 无状态 + 服务端可控踢线的桥）</summary>
[SugarTable("sys_user_session")]
public class SysUserSession : BaseEntity
{
    /// <summary>JWT jti（索引）</summary>
    [SugarColumn(Length = 64)]
    public string TokenId { get; set; } = "";

    /// <summary>RefreshToken 哈希（sha256，索引；轮换制，明文不落库）</summary>
    [SugarColumn(Length = 128)]
    public string RefreshTokenHash { get; set; } = "";

    [SugarColumn(ColumnDataType = "bigint")]
    public long UserId { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? UserName { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? LoginIp { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? UserAgent { get; set; }

    /// <summary>会话截止时间（超过则失效；refresh 顺延）</summary>
    [SugarColumn(ColumnDataType = "timestamp")]
    public DateTime ExpireTime { get; set; }
}
