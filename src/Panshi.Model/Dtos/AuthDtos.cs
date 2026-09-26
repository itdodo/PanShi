using System.ComponentModel.DataAnnotations;
using Panshi.Model.Validation;
using SqlSugar;

namespace Panshi.Model.Dtos;

/// <summary>登录请求</summary>
public class LoginDto
{
    [Required(ErrorMessage = "用户名不能为空")]
    [StringLength(64, MinimumLength = 2)]
    public string UserName { get; set; } = "";

    [Required(ErrorMessage = "密码不能为空")]
    public string Password { get; set; } = "";

    /// <summary>验证码会话 Id（sys.captcha.enabled=1 时必填）</summary>
    public string? CaptchaId { get; set; }

    public string? CaptchaCode { get; set; }
}

/// <summary>登录结果（雪花 id 均为 string）</summary>
public class LoginResultDto
{
    public string Token { get; set; } = "";

    public string RefreshToken { get; set; } = "";

    public long ExpiresIn { get; set; }

    /// <summary>密码超期/被重置 → 前端强制改密页</summary>
    public bool MustChangePassword { get; set; }
}

/// <summary>刷新令牌</summary>
public class RefreshDto
{
    [Required]
    public string Token { get; set; } = "";

    [Required]
    public string RefreshToken { get; set; } = "";
}

/// <summary>当前用户信息（/auth/profile）</summary>
public class ProfileDto
{
    public string Id { get; set; } = "";

    public string UserName { get; set; } = "";

    public string NickName { get; set; } = "";

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? DeptName { get; set; }

    /// <summary>头像文件 Id（雪花→字符串）；前端据此拉 /file/{id}/download 的字节流显示</summary>
    public string? AvatarFileId { get; set; }

    public IReadOnlyList<string> Roles { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();

    /// <summary>内置管理员（admin 本人）——前端防接管按钮显隐依据</summary>
    public bool IsAdmin { get; set; }

    public DateTime PwdUpdateTime { get; set; }
}

/// <summary>修改资料</summary>
public class UpdateProfileDto
{
    [StringLength(64)]
    public string NickName { get; set; } = "";

    [OptionalPhone]
    public string? Phone { get; set; }

    [OptionalEmailAddress]
    public string? Email { get; set; }

    /// <summary>头像文件 Id（string；null=不修改）</summary>
    public string? AvatarFileId { get; set; }

    /// <summary>乐观锁版本（编辑回显→保存回传；不传=跳过校验兼容旧客户端）</summary>
    public int? Version { get; set; }
}

/// <summary>修改密码（本人，验旧密码；也是强制改密通道）</summary>
public class ChangePasswordDto
{
    [Required]
    public string OldPassword { get; set; } = "";

    [Required]
    [StringLength(64, MinimumLength = 8)]
    public string NewPassword { get; set; } = "";
}

/// <summary>在线会话（管理端回显；id 均 string）</summary>
public class SessionDto
{
    public string Id { get; set; } = "";

    public string UserId { get; set; } = "";

    public string? UserName { get; set; }

    public string? LoginIp { get; set; }

    public string? UserAgent { get; set; }

    public DateTime CreateTime { get; set; }

    public DateTime ExpireTime { get; set; }

    /// <summary>是否当前会话</summary>
    public bool Current { get; set; }
}
