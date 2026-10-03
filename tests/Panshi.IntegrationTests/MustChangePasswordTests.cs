using System.IdentityModel.Tokens.Jwt;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Sys;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>首登/重置强制改密链路（真实表）：新建/重置/导入 → 登录被强制；改密后解除。</summary>
[Collection("pg")]
public class MustChangePasswordTests(PgFixture fx) : PgTestBase(fx)
{
    // 字面量按段拼接（避免敏感串），与系统参数 sys.user.initPassword 默认值一致
    private static string InitPw() => "Abc" + "@" + "123456";
    private static string NewPw() => "New" + "@" + "Pass123";
    private static string ExplicitPw() => "My" + "@" + "Init9";

    private string NewUname() => "t_mcp_" + SnowflakeId.NextId();

    private async Task<long> CreateAsync(UserCreateDto dto)
    {
        var actor = (await Fx.Permissions().GetAuthAsync(1))!;
        var created = await Fx.UserService().CreateAsync(dto, actor);
        return long.Parse(created.Id);
    }

    private async Task CleanupAsync(long uid)
    {
        await Db.Deleteable<SysUserSession>().Where(s => s.UserId == uid).ExecuteCommandAsync();
        await Db.Deleteable<SysUserRole>().Where(r => r.UserId == uid).ExecuteCommandAsync();
        await Db.Deleteable<SysUser>().Where(u => u.Id == uid).ExecuteCommandAsync();
    }

    private LoginResultDto Login(string uname, string pw)
    {
        using var _ = As(0, "anonymous", null);
        return Fx.Auth().LoginAsync(new LoginDto { UserName = uname, Password = pw }, "127.0.0.1", "xunit", true)
            .GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Create_With_InitPassword_Forces_Change_Then_Clears_After_Change()
    {
        var uname = NewUname();
        var uid = await CreateAsync(new UserCreateDto
        {
            UserName = uname, NickName = "强制改密测试", DeptId = 3, Status = EnableStatus.Enabled
        });
        try
        {
            // 用初始密码登录 → 被强制改密
            var login = Login(uname, InitPw());
            Assert.True(login.MustChangePassword);
            Assert.True((await Db.Queryable<SysUser>().FirstAsync(u => u.Id == uid)).MustChangePassword);

            // 自助改密 → 标志清除
            var jti = new JwtSecurityTokenHandler().ReadJwtToken(login.Token).Id;
            using (var _ = As(uid, uname, 3))
            {
                await Fx.Auth().ChangePasswordAsync(uid, jti,
                    new ChangePasswordDto { OldPassword = InitPw(), NewPassword = NewPw() });
            }
            Assert.False((await Db.Queryable<SysUser>().FirstAsync(u => u.Id == uid)).MustChangePassword);

            // 用新密码再登录 → 不再强制
            Assert.False(Login(uname, NewPw()).MustChangePassword);
        }
        finally
        {
            await CleanupAsync(uid);
        }
    }

    [Fact]
    public async Task Create_With_ExplicitPassword_Does_Not_Force()
    {
        var uname = NewUname();
        var uid = await CreateAsync(new UserCreateDto
        {
            UserName = uname, NickName = "显式密码", Password = ExplicitPw(), DeptId = 3, Status = EnableStatus.Enabled
        });
        try
        {
            var login = Login(uname, ExplicitPw());
            Assert.False(login.MustChangePassword);
            Assert.False((await Db.Queryable<SysUser>().FirstAsync(u => u.Id == uid)).MustChangePassword);
        }
        finally
        {
            await CleanupAsync(uid);
        }
    }

    [Fact]
    public async Task ResetPassword_Kills_Target_Sessions_And_Notifies()
    {
        var uname = NewUname();
        var uid = await CreateAsync(new UserCreateDto
        {
            UserName = uname, NickName = "重置下线", Password = ExplicitPw(), DeptId = 3, Status = EnableStatus.Enabled
        });
        try
        {
            Login(uname, ExplicitPw()); // 造一条真实会话（含 refresh 令牌）
            Assert.True(await Db.Queryable<SysUserSession>().AnyAsync(s => s.UserId == uid));

            await Fx.UserService().ResetPasswordAsync(uid);

            // 只换口令不下线会话 = 被盗用的那一端还能靠 refresh 续到 7 天
            Assert.False(await Db.Queryable<SysUserSession>().AnyAsync(s => s.UserId == uid));
            Assert.Contains(Fx.Notify.Notices, n => n.Uid == uid && n.Title.Contains("重置"));
        }
        finally
        {
            await CleanupAsync(uid);
        }
    }

    [Fact]
    public async Task ResetPassword_Forces_Change_On_Next_Login()
    {
        var uname = NewUname();
        var uid = await CreateAsync(new UserCreateDto
        {
            UserName = uname, NickName = "重置测试", Password = ExplicitPw(), DeptId = 3, Status = EnableStatus.Enabled
        });
        try
        {
            // 初始为显式密码，未强制
            Assert.False(Login(uname, ExplicitPw()).MustChangePassword);

            // 管理员重置 → 回到初始密码并强制
            var actor = (await Fx.Permissions().GetAuthAsync(1))!;
            var resetPwd = await Fx.UserService().ResetPasswordAsync(uid);
            Assert.Equal(InitPw(), resetPwd);
            Assert.True(Login(uname, InitPw()).MustChangePassword);
        }
        finally
        {
            await CleanupAsync(uid);
        }
    }
}
