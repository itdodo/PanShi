using System.ComponentModel.DataAnnotations;
using Panshi.Model.Enums;
using Panshi.Model.Validation;

namespace Panshi.Model.Dtos;

/// <summary>用户查询</summary>
public class UserQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public EnableStatus? Status { get; set; }

    public long? DeptId { get; set; }

    /// <summary>含部门子树</summary>
    public bool DeptWithChildren { get; set; }
}

/// <summary>用户回显（id 全 string；Version 乐观锁回传）</summary>
public class UserDto
{
    public string Id { get; set; } = "";

    public string UserName { get; set; } = "";

    public string NickName { get; set; } = "";

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? DeptId { get; set; }

    public string? DeptName { get; set; }

    public EnableStatus Status { get; set; }

    public string? AvatarFileId { get; set; }

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime PwdUpdateTime { get; set; }

    public DateTime? LastLoginTime { get; set; }

    public List<string> RoleIds { get; set; } = [];

    public List<string> PositionIds { get; set; } = [];

    public int Version { get; set; }
}

/// <summary>新增用户</summary>
public class UserCreateDto
{
    [Required, StringLength(64, MinimumLength = 2)]
    public string UserName { get; set; } = "";

    /// <summary>留空=系统参数初始密码</summary>
    public string? Password { get; set; }

    [Required, StringLength(64)]
    public string NickName { get; set; } = "";

    [OptionalPhone]
    public string? Phone { get; set; }

    [OptionalEmailAddress]
    public string? Email { get; set; }

    public long? DeptId { get; set; }

    public EnableStatus Status { get; set; }

    public string? Remark { get; set; }

    public List<long> RoleIds { get; set; } = [];

    public List<long> PositionIds { get; set; } = [];
}

/// <summary>编辑用户（⚠️ Version 必传回显值——红线 #6）</summary>
public class UserUpdateDto
{
    [Required, StringLength(64)]
    public string NickName { get; set; } = "";

    [OptionalPhone]
    public string? Phone { get; set; }

    [OptionalEmailAddress]
    public string? Email { get; set; }

    public long? DeptId { get; set; }

    public EnableStatus Status { get; set; }

    public string? Remark { get; set; }

    public List<long> RoleIds { get; set; } = [];

    public List<long> PositionIds { get; set; } = [];

    public int Version { get; set; }
}

/// <summary>分配角色</summary>
public class AssignRolesDto
{
    public List<long> RoleIds { get; set; } = [];

    /// <summary>乐观锁（分配角色走内部列更新，仅用于陈旧检测回传）</summary>
    public int? Version { get; set; }
}

/// <summary>导入行（MiniExcel）</summary>
public class UserImportRow
{
    public string UserName { get; set; } = "";

    public string NickName { get; set; } = "";

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? DeptCode { get; set; }

    public string? Status { get; set; }
}

/// <summary>导入结果</summary>
public class ImportResultDto
{
    public int Total { get; set; }

    public int Success { get; set; }

    public List<string> Errors { get; set; } = [];
}

/// <summary>简单下拉项（id 统一 string）</summary>
public sealed class OptionDto
{
    public string Value { get; set; } = "";

    public string Label { get; set; } = "";
}
