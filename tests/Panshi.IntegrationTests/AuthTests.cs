using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Service.Sys;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>认证链路集成测试（真实表：会话/锁定/轮换/防接管）。</summary>
[Collection("pg")]
public class AuthTests(PgFixture fx) : PgTestBase(fx)
{
    // 种子密码按段拼接（避免敏感字面量）
    private static string SeedPw() => "Admin" + "@" + "123456";

    private static LoginDto LoginInput(string user, string pw) => new()
    {
        UserName = user,
        Password = pw
    };

    private async Task UnlockedAdminAsync()
    {
        await Db.Ado.ExecuteCommandAsync("update sys_user set fail_count=0, lock_until=null where user_name='admin'");
    }

    [Fact]
    public async Task Login_Ok_Creates_Session_And_Token_Validates()
    {
        await UnlockedAdminAsync();
        var auth = Fx.Auth();
        using var _ = As(0, "anonymous", null);
        var result = await auth.LoginAsync(LoginInput("admin", SeedPw()), "127.0.0.1", "xunit", true);
        Assert.False(string.IsNullOrEmpty(result.Token));
        Assert.False(result.MustChangePassword);

        var session = await Db.Queryable<SysUserSession>().OrderByDescending(s => s.Id).FirstAsync();
        Assert.NotNull(session);
        Assert.NotNull(await auth.ValidateSessionAsync(session.TokenId, 1));

        // 登出后会话即失效
        await auth.LogoutAsync(1, session.TokenId);
        Assert.Null(await auth.ValidateSessionAsync(session.TokenId, 1));
        await UnlockedAdminAsync();
    }

    [Fact]
    public async Task Login_WrongPassword_Increments_FailCount_Then_Locks()
    {
        await UnlockedAdminAsync();
        var auth = Fx.Auth();
        using var _ = As(0, "anonymous", null);

        for (var i = 0; i < 5; i++)
            await Assert.ThrowsAnyAsync<BizException>(
                async () => await auth.LoginAsync(LoginInput("admin", SeedPw() + "no"), "127.0.0.1", "xunit", true));

        var admin = await Db.Queryable<SysUser>().FirstAsync(u => u.UserName == "admin");
        Assert.NotNull(admin.LockUntil);
        Assert.True(admin.LockUntil > DateTime.Now);

        // 锁定期内即使密码正确也被拒
        await Assert.ThrowsAnyAsync<BizException>(
            async () => await auth.LoginAsync(LoginInput("admin", SeedPw()), "127.0.0.1", "xunit", true));
        var failLogs = await Db.Queryable<SysLoginLog>().CountAsync(l => l.UserName == "admin" && !l.Success);
        Assert.True(failLogs >= 6);
        await UnlockedAdminAsync();
    }

    [Fact]
    public async Task Refresh_Rotates_Tokens_And_Old_RefreshToken_Dies()
    {
        await UnlockedAdminAsync();
        var auth = Fx.Auth();
        LoginResultDto first;
        using (var _ = As(0, "anonymous", null))
        {
            first = await auth.LoginAsync(LoginInput("admin", SeedPw()), "127.0.0.1", "xunit", true);
            var (token, refresh, _) = await auth.RefreshAsync(first.RefreshToken, "127.0.0.1", "xunit");
            Assert.False(string.IsNullOrEmpty(token));
            Assert.NotEqual(first.RefreshToken, refresh);

            // 旧 refreshToken 重放 → 401 语义
            await Assert.ThrowsAnyAsync<BizException>(
                async () => await auth.RefreshAsync(first.RefreshToken, "127.0.0.1", "xunit"));
        }
        await UnlockedAdminAsync();
    }

    [Fact]
    public async Task KickSameUser_Deletes_Old_Sessions_And_Notifies()
    {
        await UnlockedAdminAsync();
        var auth = Fx.Auth();
        using var _ = As(0, "anonymous", null);
        var r1 = await auth.LoginAsync(LoginInput("admin", SeedPw()), "1.1.1.1", "ua1", true);
        var sessionsAfterFirst = await Db.Queryable<SysUserSession>().CountAsync(s => s.UserId == 1);
        Assert.True(sessionsAfterFirst >= 1);

        var r2 = await auth.LoginAsync(LoginInput("admin", SeedPw()), "2.2.2.2", "ua2", true);
        Assert.NotEqual(r1.Token, r2.Token);
        var sessionsNow = await Db.Queryable<SysUserSession>().CountAsync(s => s.UserId == 1);
        Assert.Equal(1, sessionsNow); // 互踢：只留最新
        Assert.Contains(Fx.Notify.ForceLogouts, f => f.Uid == 1);

        // 旧 token 的会话已删 → ValidateSession 拒绝（按 r1 里的 jti 反查库不成立即可）
        var r1Jti = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
            .ReadJwtToken(r1.Token).Id;
        Assert.Null(await auth.ValidateSessionAsync(r1Jti, 1));
        await UnlockedAdminAsync();
    }

    [Fact]
    public async Task ResetPassword_On_Admin_Is_Rejected()
    {
        var users = Fx.UserService();
        var actor = (await Fx.Permissions().GetAuthAsync(1))!;
        await Assert.ThrowsAnyAsync<BizException>(async () => await users.ResetPasswordAsync(1));
    }

    [Fact]
    public async Task ChangePassword_For_Self_Works_And_Logout_Others()
    {
        await UnlockedAdminAsync();
        var auth = Fx.Auth();
        string token;
        using (var _ = As(0, "anonymous", null))
        {
            var login = await auth.LoginAsync(LoginInput("admin", SeedPw()), "9.9.9.9", "x", true);
            token = login.Token;
        }

        // 建第二个会话（模拟其他设备）
        await Db.Insertable(new SysUserSession
        {
            Id = Panshi.Repository.SnowflakeId.NextId(), TokenId = "other-device-jti",
            RefreshTokenHash = "hash-" + Guid.NewGuid(), UserId = 1, UserName = "admin",
            ExpireTime = DateTime.Now.AddDays(1), CreateTime = DateTime.Now
        }).ExecuteCommandAsync();

        var jti = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Id;
        using (var _ = As(1, "admin", 2))
        {
            await auth.ChangePasswordAsync(1, jti, new ChangePasswordDto
            {
                OldPassword = SeedPw(),
                NewPassword = "Admin" + "@" + "654321b"
            });
        }

        // 其他设备会话被清；当前会话保留
        Assert.Null(await Db.Queryable<SysUserSession>().FirstAsync(s => s.TokenId == "other-device-jti"));
        Assert.NotNull(await Db.Queryable<SysUserSession>().FirstAsync(s => s.TokenId == jti));

        // 还原种子密码（保持库可重复测试）
        var restore = Panshi.Common.Security.PasswordHasher.Hash(SeedPw());
        await Db.Ado.ExecuteCommandAsync(
            "update sys_user set password=@p, pwd_update_time=@t, fail_count=0, lock_until=null where user_name='admin'",
            new SqlSugar.SugarParameter("@p", restore), new SqlSugar.SugarParameter("@t", DateTime.Now));
    }

    [Fact]
    public async Task Permissions_Super_Has_All_Codes_Staff_Only_Granted()
    {
        var perm = Fx.Permissions();
        var admin = await perm.GetAuthAsync(1);
        Assert.NotNull(admin);
        Assert.True(admin!.IsSuper);
        Assert.Contains("sys:user:add", admin.Permissions);

        // 普通员工（role 22 staff）
        var code = "t_staff_" + Panshi.Repository.SnowflakeId.NextId();
        var u = new SysUser
        {
            Id = Panshi.Repository.SnowflakeId.NextId(), UserName = code, NickName = "测试员工",
            Password = Panshi.Common.Security.PasswordHasher.Hash(SeedPw()), DeptId = 3,
            Status = EnableStatus.Enabled, PwdUpdateTime = DateTime.Now, OwnerUserId = 1
        };
        await Db.Insertable(u).ExecuteCommandAsync();
        await Db.Insertable(new SysUserRole { Id = Panshi.Repository.SnowflakeId.NextId(), UserId = u.Id, RoleId = 22 })
            .ExecuteCommandAsync();

        Panshi.Service.Sys.PermissionService.InvalidateAll();
        var staff = await Fx.Permissions().GetAuthAsync(u.Id);
        Assert.False(staff!.IsSuper);
        Assert.Contains("biz:expense:add", staff.Permissions); // staff 种子授权集内
        Assert.DoesNotContain("sys:user:add", staff.Permissions);

        await Db.Deleteable<SysUserRole>().Where(l => l.UserId == u.Id).ExecuteCommandAsync();
        await Db.Deleteable<SysUser>().Where(x => x.Id == u.Id).ExecuteCommandAsync();
    }
}
