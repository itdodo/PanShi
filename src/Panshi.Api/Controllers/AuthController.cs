using System.Security.Claims;
using Lazy.Captcha.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Panshi.Api.Middleware;
using Panshi.Api.Security;
using Panshi.Api.Services;
using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Repository;
using Panshi.Service.Sys;

namespace Panshi.Api.Controllers;

/// <summary>认证与会话治理（蓝图§六 /auth）。</summary>
[ApiController]
[Route("api/v1/auth")]
[Authorize]
[Tags("认证")]
public class AuthController(
    AuthService auth,
    ICaptcha captcha,
    FileStorage storage,
    IRepository<SysFile> fileRepo,
    ConfigService config) : ApiControllerBase
{
    private string Jti => HttpContext.CurrentTokenId() ?? throw BizException.Unauthorized();

    private string Ip => ClientIp.Of(HttpContext);

    private string Ua => Request.Headers.UserAgent.ToString();

    /// <summary>
    /// 图形验证码（Lazy.Captcha，内存存储）。
    /// sys.captcha.enabled=0 时不生成图，直接 204 + X-Captcha-Enabled: 0，登录页据此隐藏整行——
    /// 既省掉一次无用生成，也不在关闭状态下继续暴露一个可被刷的生成端点。
    /// </summary>
    [HttpGet("captcha")]
    [AllowAnonymous]
    [EnableRateLimiting("captcha")]
    public async Task<IActionResult> Captcha()
    {
        var enabled = await config.GetBoolAsync("sys.captcha.enabled", true);
        Response.Headers["X-Captcha-Enabled"] = enabled ? "1" : "0";
        if (!enabled) return NoContent();

        var data = captcha.Generate(Guid.NewGuid().ToString("N"));
        Response.Headers["X-Captcha-Id"] = data.Id;
        return File(data.Bytes, "image/gif");
    }

    /// <summary>登录（防爆破：失败计数锁定；接口限流；验证码参数可关）</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<LoginResultDto> Login([FromBody] LoginDto dto)
    {
        var captchaOk = !await config.GetBoolAsync("sys.captcha.enabled", true)
                        || (!string.IsNullOrEmpty(dto.CaptchaId) && captcha.Validate(dto.CaptchaId, dto.CaptchaCode ?? ""));
        return await auth.LoginAsync(dto, Ip, Ua, captchaOk);
    }

    /// <summary>刷新令牌（轮换制）</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<ApiResult<LoginResultDto>> Refresh([FromBody] RefreshDto dto)
    {
        var (token, refreshToken, mustChange) = await auth.RefreshAsync(dto.RefreshToken, Ip, Ua);
        return ApiResult.Ok(new LoginResultDto
        {
            Token = token, RefreshToken = refreshToken,
            ExpiresIn = (long)TimeSpan.FromHours(2).TotalSeconds, MustChangePassword = mustChange
        });
    }

    [HttpPost("logout")]
    public async Task Logout()
    {
        await auth.LogoutAsync(Uid, Jti);
    }

    /// <summary>当前用户信息（角色+权限码；无角色 users 权限码为空数组）</summary>
    [HttpGet("profile")]
    public async Task<ProfileDto> Profile() => await auth.GetProfileAsync(Uid);

    [HttpPut("profile")]
    public async Task UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        await auth.UpdateProfileAsync(Uid, dto);
    }

    /// <summary>修改密码（本人验旧密码——也是 admin 账号唯一的改密通道，防接管）</summary>
    [HttpPost("change-password")]
    public async Task ChangePassword([FromBody] ChangePasswordDto dto)
    {
        await auth.ChangePasswordAsync(Uid, Jti, dto);
    }

    /// <summary>头像上传（白名单+嗅探，2MB）</summary>
    [HttpPost("avatar")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<ApiResult<string>> Avatar(IFormFile file)
    {
        if (file is null) throw new BizException("请选择文件");
        var meta = await storage.SaveAsync(file, "avatar",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp" },
            2 * 1024 * 1024);
        var saved = await fileRepo.InsertAsync(meta);
        await auth.SetAvatarAsync(Uid, saved.Id);
        return ApiResult.Ok(saved.Id.ToString());
    }

    /// <summary>我的在线会话（含“是否当前会话”）</summary>
    [HttpGet("sessions")]
    public async Task<IReadOnlyList<SessionDto>> Sessions() => await auth.ListSessionsAsync(Uid, Jti);

    /// <summary>下线自己的某个会话</summary>
    [HttpDelete("sessions/{id:long}")]
    public async Task KickOwnSession(long id) => await auth.KickSessionAsync(id, Uid);
}
