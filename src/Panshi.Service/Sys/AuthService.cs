using Panshi.Common.Exceptions;
using Panshi.Common.Realtime;
using Panshi.Common.Security;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Auth;
using Panshi.Service.Base;
using Panshi.Service.Sys;

namespace Panshi.Service.Sys;

/// <summary>
/// 认证与会话治理（蓝图§5.3）：
/// JWT(2h)+RefreshToken(7天轮换)+会话表；防爆破；密码有效期强制改密；单点在线互踢。
/// </summary>
public class AuthService(
    IRepository<SysUser> userRepo,
    IRepository<SysUserRole> userRoleRepo,
    IRepository<SysRole> roleRepo,
    IRepository<SysRoleMenu> roleMenuRepo,
    IRepository<SysMenu> menuRepo,
    IRepository<SysDept> deptRepo,
    IRepository<SysUserSession> sessionRepo,
    IRepository<SysLoginLog> loginLogRepo,
    TokenService tokenService,
    ConfigService config,
    INotifyService notify) : BaseService<SysUser>(userRepo)
{
    public const string AdminUserName = "admin";

    // ---------------- 登录 ----------------
    public async Task<LoginResultDto> LoginAsync(LoginDto dto, string ip, string userAgent, bool captchaOk)
    {
        var user = await Repo.FindAsync(u => u.UserName == dto.UserName);

        if (await config.GetBoolAsync("sys.captcha.enabled", true) && !captchaOk)
        {
            await LogAsync(dto.UserName, user, ip, userAgent, "验证码错误");
            throw new BizException("验证码错误");
        }

        if (user is null)
        {
            await LogAsync(dto.UserName, null, ip, userAgent, "用户名或密码错误");
            throw new BizException("用户名或密码错误");
        }

        if (user.LockUntil is { } lockUntil && lockUntil > DateTime.Now)
        {
            await LogAsync(dto.UserName, user, ip, userAgent, "账号已锁定");
            throw new BizException($"账号已锁定，请于 {lockUntil:HH:mm} 后重试");
        }

        if (user.Status != EnableStatus.Enabled)
        {
            await LogAsync(dto.UserName, user, ip, userAgent, "账号已停用");
            throw new BizException("账号已停用，联系管理员");
        }

        if (!PasswordHasher.Verify(dto.Password, user.Password))
        {
            await RecordFailureAsync(user, ip, userAgent);
            throw new BizException("用户名或密码错误");
        }

        // 防爆破状态复位 + 最后登录时间（内部列更新，不动 Version）
        user.FailCount = 0;
        user.LockUntil = null;
        user.LastLoginTime = DateTime.Now;
        var cols = new List<string> { "FailCount", "LockUntil", "LastLoginTime" };
        if (PasswordHasher.NeedsRehash(user.Password))
        {
            user.Password = PasswordHasher.Hash(dto.Password); // 历史哈希透明升级
            cols.Add("Password");
        }

        var mustChange = await MustChangePasswordAsync(user);

        await KickSameUserAsync(user.Id, $"账号 {user.UserName} 在其他位置登录");
        await Repo.UpdateColumnsAsync(user, [.. cols]);

        // 令牌与会话必须共用同一 jti/refreshToken（OnTokenValidated 按会话表校验）
        var tokenId = TokenService.NewTokenId();
        var refreshToken = TokenService.NewRefreshToken();
        await sessionRepo.InsertAsync(new SysUserSession
        {
            TokenId = tokenId,
            RefreshTokenHash = TokenService.HashToken(refreshToken),
            UserId = user.Id,
            UserName = user.UserName,
            LoginIp = ip,
            UserAgent = userAgent,
            ExpireTime = DateTime.Now.AddDays(tokenService.Options.RefreshTokenDays)
        });

        await LogAsync(user.UserName, user, ip, userAgent, "登录成功", true);
        return new LoginResultDto
        {
            Token = tokenService.CreateAccessToken(user, tokenId),
            RefreshToken = refreshToken,
            ExpiresIn = tokenService.Options.AccessTokenMinutes * 60L,
            MustChangePassword = mustChange
        };
    }

    private async Task RecordFailureAsync(SysUser user, string ip, string userAgent)
    {
        var limit = await config.GetIntAsync("sys.login.failLimit", 5);
        var minutes = await config.GetIntAsync("sys.login.lockMinutes", 15);
        user.FailCount += 1;
        string result = "密码错误";
        if (limit > 0 && user.FailCount >= limit)
        {
            user.LockUntil = DateTime.Now.AddMinutes(minutes);
            user.FailCount = 0;
            result = $"密码错误达 {limit} 次，锁定 {minutes} 分钟";
        }

        await Repo.UpdateColumnsAsync(user, "FailCount", "LockUntil");
        await LogAsync(user.UserName, user, ip, userAgent, result);
        throw new BizException(result.Contains("锁定")
            ? $"密码错误次数过多，账号已锁定 {minutes} 分钟"
            : limit > 0 ? $"用户名或密码错误（再错 {limit - user.FailCount} 次将锁定）" : "用户名或密码错误");
    }

    // ---------------- 令牌与会话 ----------------
    /// <summary>同账号互踢（sys.login.kickSameUser=1）：删旧会话 + force-logout 推送。</summary>
    private async Task KickSameUserAsync(long userId, string reason)
    {
        if (!await config.GetBoolAsync("sys.login.kickSameUser", true)) return;
        var sessions = await sessionRepo.ListAsync(s => s.UserId == userId);
        if (sessions.Count == 0) return;
        await sessionRepo.DeleteAsync(s => s.UserId == userId);
        await notify.ForceLogoutAsync(userId, reason);
    }

    public async Task<(string Token, string RefreshToken, bool MustChange)> RefreshAsync(string refreshToken,
        string ip, string userAgent)
    {
        var hash = TokenService.HashToken(refreshToken);
        var session = await sessionRepo.FindAsync(s => s.RefreshTokenHash == hash && s.ExpireTime > DateTime.Now)
            ?? throw BizException.Unauthorized("登录已过期，请重新登录");

        var user = await Repo.GetAsync(session.UserId);
        if (user.Status != EnableStatus.Enabled)
        {
            await sessionRepo.DeleteAsync(session.Id);
            throw BizException.Unauthorized("账号已停用");
        }

        var newTokenId = TokenService.NewTokenId();
        var newRefresh = TokenService.NewRefreshToken();
        session.TokenId = newTokenId;
        session.RefreshTokenHash = TokenService.HashToken(newRefresh);
        session.ExpireTime = DateTime.Now.AddDays(tokenService.Options.RefreshTokenDays);
        session.LoginIp = ip;
        await sessionRepo.UpdateColumnsAsync(session, "TokenId", "RefreshTokenHash", "ExpireTime", "LoginIp");

        var mustChange = await MustChangePasswordAsync(user);
        return (tokenService.CreateAccessToken(user, newTokenId), newRefresh, mustChange);
    }

    /// <summary>会话有效性（OnTokenValidated 调用）：不存在即 401——登出/踢线/停用即时生效。</summary>
    public async Task<SysUser?> ValidateSessionAsync(string tokenId, long userId)
    {
        var session = await sessionRepo.FindAsync(s => s.TokenId == tokenId);
        if (session is null || session.ExpireTime <= DateTime.Now || session.UserId != userId) return null;

        var user = await Repo.FindAsync(userId);
        return user is { Status: EnableStatus.Enabled } ? user : null;
    }

    public async Task LogoutAsync(long userId, string tokenId)
    {
        await sessionRepo.DeleteAsync(s => s.UserId == userId && s.TokenId == tokenId);
    }

    public async Task<IReadOnlyList<SessionDto>> ListSessionsAsync(long userId, string currentTokenId)
    {
        var sessions = await sessionRepo.ListAsync(s => s.UserId == userId);
        return sessions
            .OrderByDescending(s => s.CreateTime)
            .Select(s => new SessionDto
            {
                Id = s.Id.ToString(), UserId = s.UserId.ToString(), UserName = s.UserName,
                LoginIp = s.LoginIp, UserAgent = s.UserAgent, CreateTime = s.CreateTime,
                ExpireTime = s.ExpireTime, Current = s.TokenId == currentTokenId
            })
            .ToList();
    }

    /// <summary>本人踢线（其他用户的会话下线走 monitor/online 强退，控制器分权）。</summary>
    public async Task KickSessionAsync(long sessionId, long requesterUserId)
    {
        var session = await sessionRepo.GetAsync(sessionId);
        if (session.UserId != requesterUserId) throw BizException.Forbidden("只能下线自己的会话");
        await KickSessionByAdminAsync(sessionId);
    }

    /// <summary>管理端强退（monitor:online:kick）。</summary>
    public async Task KickSessionByAdminAsync(long sessionId)
    {
        var session = await sessionRepo.GetAsync(sessionId);
        await sessionRepo.DeleteAsync(session.Id);
        await notify.ForceLogoutAsync(session.UserId, "管理员已将您下线");
    }

    // ---------------- 资料 / 密码 ----------------
    public async Task<ProfileDto> GetProfileAsync(long userId)
    {
        var user = await Repo.GetAsync(userId);
        var (roles, permissions) = await LoadAuthAsync(user);
        string? deptName = null;
        if (user.DeptId is long deptId)
            deptName = (await deptRepo.FindAsync(deptId))?.DeptName;

        return new ProfileDto
        {
            Id = user.Id.ToString(), UserName = user.UserName, NickName = user.NickName,
            Phone = user.Phone, Email = user.Email, DeptName = deptName,
            AvatarFileId = user.AvatarFileId?.ToString(),
            Roles = roles, Permissions = permissions,
            IsAdmin = user.UserName == AdminUserName,
            PwdUpdateTime = user.PwdUpdateTime
        };
    }

    /// <summary>角色编码 + 权限码（admin 角色=全量；普通用户=角色并集）。权限引擎批次会加缓存。</summary>
    public async Task<(IReadOnlyList<string> Roles, IReadOnlyList<string> Permissions)> LoadAuthAsync(SysUser user)
    {
        var links = await userRoleRepo.ListAsync(l => l.UserId == user.Id);
        var roleIds = links.Select(l => l.RoleId).ToHashSet();
        var roles = (await roleRepo.ListAsync()).Where(r => roleIds.Contains(r.Id) && r.Status == EnableStatus.Enabled)
            .Select(r => r.RoleCode).ToList();

        if (roles.Contains("admin") || user.UserName == AdminUserName)
        {
            var all = (await menuRepo.ListAsync()).Where(m => !string.IsNullOrEmpty(m.Permission))
                .Select(m => m.Permission!).Distinct().ToList();
            return (roles, all);
        }

        var roleMenus = await roleMenuRepo.ListAsync(rm => roleIds.Contains(rm.RoleId));
        var menuIds = roleMenus.Select(rm => rm.MenuId).ToHashSet();
        var perms = (await menuRepo.ListAsync())
            .Where(m => menuIds.Contains(m.Id) && !string.IsNullOrEmpty(m.Permission) &&
                        m.Status == EnableStatus.Enabled)
            .Select(m => m.Permission!).Distinct().ToList();
        return (roles, perms);
    }

    public async Task UpdateProfileAsync(long userId, UpdateProfileDto dto)
    {
        var user = await Repo.GetAsync(userId);
        user.NickName = dto.NickName;
        user.Phone = dto.Phone;
        user.Email = dto.Email;
        if (long.TryParse(dto.AvatarFileId, out var avatar)) user.AvatarFileId = avatar;

        if (dto.Version is int ver)
        {
            user.Version = ver; // 期望版本→乐观锁+字段审计统一入口（红线 #6）
            await UpdateWithConcurrencyCheckAsync(user);
        }
        else
        {
            await Repo.UpdateColumnsAsync(user, "NickName", "Phone", "Email", "AvatarFileId");
        }
    }

    /// <summary>头像列更新（内部维护，不动 Version）。</summary>
    public async Task SetAvatarAsync(long userId, long fileId)
    {
        var user = await Repo.GetAsync(userId);
        user.AvatarFileId = fileId;
        await Repo.UpdateColumnsAsync(user, "AvatarFileId");
    }

    public async Task ChangePasswordAsync(long userId, string currentTokenId, ChangePasswordDto dto)
    {
        var user = await Repo.GetAsync(userId);
        if (!PasswordHasher.Verify(dto.OldPassword, user.Password))
            throw new BizException("旧密码不正确");
        if (string.Equals(dto.OldPassword, dto.NewPassword, StringComparison.Ordinal))
            throw new BizException("新密码不能与旧密码相同");
        var (ok, msg) = PasswordHasher.ValidatePolicy(dto.NewPassword);
        if (!ok) throw new BizException(msg);

        user.Password = PasswordHasher.Hash(dto.NewPassword);
        user.PwdUpdateTime = DateTime.Now;
        user.MustChangePassword = false; // 改密成功即解除强制
        await Repo.UpdateColumnsAsync(user, "Password", "PwdUpdateTime", "MustChangePassword");

        // 其他设备全部下线（保留当前会话）
        var others = await sessionRepo.ListAsync(s => s.UserId == userId && s.TokenId != currentTokenId);
        if (others.Count > 0)
        {
            await sessionRepo.DeleteAsync(s => s.UserId == userId && s.TokenId != currentTokenId);
            await notify.ForceLogoutAsync(userId, "密码已修改，其他设备需重新登录");
        }
    }

    /// <summary>
    /// 「这个账号现在必须先改密码吗」——登录回给前端只是提示，真正的拦在请求管道里
    /// （AuthSetup 的 OnTokenValidated 每条请求都问一次），否则手搓请求就绕过了。
    /// </summary>
    public async Task<bool> MustChangePasswordAsync(SysUser user)
    {
        if (user.MustChangePassword) return true; // 新建/重置/导入后的首登强制改密
        var days = await config.GetIntAsync("sys.pwd.expireDays", 90);
        return days > 0 && user.PwdUpdateTime.AddDays(days) < DateTime.Now;
    }

    private Task LogAsync(string userName, SysUser? user, string ip, string ua, string result,
        bool success = false)
        => loginLogRepo.InsertAsync(new SysLoginLog
        {
            Id = SnowflakeId.NextId(),
            UserName = userName, Result = result, Success = success, Ip = ip, UserAgent = ua,
            CreateBy = user?.Id
        });
}
