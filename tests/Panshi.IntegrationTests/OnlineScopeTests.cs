using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 在线会话列表的数据权限回归。此前它是「有 monitor:online:list 菜单就看全员」，
/// 而同域的登录日志早已按可见用户收敛——两处口径不一致。
/// </summary>
[Collection("pg")]
public class OnlineScopeTests(PgFixture fx) : PgTestBase(fx)
{
    private static string Tag() => "on" + SnowflakeId.NextId();

    private async Task<(SysUser user, SysRole role)> UserAsync(DataScopeType scope, long? deptId, string tag)
    {
        var user = new SysUser
        {
            Id = SnowflakeId.NextId(), UserName = "u_" + tag, NickName = "在线夹具",
            Password = "not-used", DeptId = deptId, Status = EnableStatus.Enabled, PwdUpdateTime = DateTime.Now
        };
        user.OwnerUserId = user.Id;
        var role = new SysRole
        {
            Id = SnowflakeId.NextId(), RoleCode = "r_" + tag, RoleName = "在线夹具角色",
            DataScope = scope, Status = EnableStatus.Enabled, Sort = 999
        };
        await Db.Insertable(user).ExecuteCommandAsync();
        await Db.Insertable(role).ExecuteCommandAsync();
        await Db.Insertable(new SysUserRole
        {
            Id = SnowflakeId.NextId(), UserId = user.Id, RoleId = role.Id
        }).ExecuteCommandAsync();
        return (user, role);
    }

    private async Task<SysUserSession> SessionAsync(SysUser u)
    {
        var s = new SysUserSession
        {
            Id = SnowflakeId.NextId(), TokenId = "t_" + u.Id, RefreshTokenHash = "h_" + u.Id,
            UserId = u.Id, UserName = u.UserName, LoginIp = "10.0.0.1", UserAgent = "OnlineScopeTests",
            CreateTime = DateTime.Now, ExpireTime = DateTime.Now.AddHours(2)
        };
        await Db.Insertable(s).ExecuteCommandAsync();
        return s;
    }

    [Fact]
    public async Task Online_List_Narrows_To_Callers_Dept()
    {
        var tag = Tag();
        var deptA = 943_000_001L;
        var deptB = 943_000_002L;
        var (me, role) = await UserAsync(DataScopeType.Dept, deptA, tag + "a");
        var (peerA, roleA) = await UserAsync(DataScopeType.Dept, deptA, tag + "b");
        var (peerB, roleB) = await UserAsync(DataScopeType.Dept, deptB, tag + "c");
        var sA = await SessionAsync(peerA);
        var sB = await SessionAsync(peerB);
        try
        {
            var mine = await Fx.Online().ListAsync(me.Id);
            Assert.Contains(mine, x => x.UserId == peerA.Id.ToString()); // 同部门在
            Assert.DoesNotContain(mine, x => x.UserId == peerB.Id.ToString()); // 别部门不在
            Assert.DoesNotContain(mine, x => x.UserId == "1"); // admin（总经办）也不该漏进来

            // 超管 = All 档，仍看得到那条别部门的会话：收敛没有把管理视角一并砍掉
            var super = await Fx.Online().ListAsync(1);
            Assert.Contains(super, x => x.UserId == peerB.Id.ToString());
        }
        finally
        {
            await Db.Deleteable<SysUserSession>().In(new List<long> { sA.Id, sB.Id }).ExecuteCommandAsync();
            foreach (var u in new[] { me, peerA, peerB })
                await Db.Deleteable<SysUser>().Where(x => x.Id == u.Id).ExecuteCommandAsync();
            foreach (var r in new[] { role, roleA, roleB })
                await Db.Deleteable<SysRole>().Where(x => x.Id == r.Id).ExecuteCommandAsync();
        }
    }

    [Fact]
    public async Task Online_List_Dept_Scope_Without_Dept_Sees_Nothing()
    {
        var tag = Tag();
        var (me, role) = await UserAsync(DataScopeType.Dept, null, tag); // 无部门 + 本部门档
        try
        {
            var list = await Fx.Online().ListAsync(me.Id);
            Assert.Empty(list);
        }
        finally
        {
            await Db.Deleteable<SysUser>().Where(x => x.Id == me.Id).ExecuteCommandAsync();
            await Db.Deleteable<SysRole>().Where(x => x.Id == role.Id).ExecuteCommandAsync();
        }
    }

    /// <summary>
    /// 幽灵会话：删号/停用后 ValidateSessionAsync 已让令牌失效，但会话行要到自然过期才消失。
    /// 不剔除的话，All 档（admin）的在线列表会把已注销账号显示成在线。
    /// </summary>
    [Fact]
    public async Task Online_List_Hides_Sessions_Of_Deleted_Users_Even_For_All_Scope()
    {
        var tag = Tag();
        var (ghost, role) = await UserAsync(DataScopeType.Self, 3, tag);
        var session = await SessionAsync(ghost);
        try
        {
            // 先确认它在：说明这条用例真的在测「剔除」而不是「从来没建上」
            Assert.Contains(await Fx.Online().ListAsync(1), x => x.UserId == ghost.Id.ToString());

            await Db.Updateable<SysUser>()
                .SetColumns(u => new SysUser { IsDeleted = true })
                .Where(u => u.Id == ghost.Id).ExecuteCommandAsync();

            Assert.DoesNotContain(await Fx.Online().ListAsync(1), x => x.UserId == ghost.Id.ToString());
        }
        finally
        {
            await Db.Deleteable<SysUserSession>().Where(s => s.Id == session.Id).ExecuteCommandAsync();
            await Db.Deleteable<SysUser>().Where(x => x.Id == ghost.Id).ExecuteCommandAsync();
            await Db.Deleteable<SysRole>().Where(x => x.Id == role.Id).ExecuteCommandAsync();
        }
    }
}
