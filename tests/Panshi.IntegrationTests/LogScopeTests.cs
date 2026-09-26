using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 日志三表的数据权限回归。它们只有 user_name（没有 dept_id/owner_user_id），
/// 靠 DataScopeService.VisibleUserNamesAsync 收敛；修复前任何人拿到 monitor:*log:list 就能看到全员记录。
/// </summary>
[Collection("pg")]
public class LogScopeTests(PgFixture fx) : PgTestBase(fx)
{
    private static string Tag() => "lg" + SnowflakeId.NextId();

    private async Task<SysUser> UserAsync(DataScopeType scope, long? deptId, string tag)
    {
        var user = new SysUser
        {
            Id = SnowflakeId.NextId(), UserName = "u_" + tag, NickName = "日志夹具",
            Password = "not-used", DeptId = deptId, Status = EnableStatus.Enabled, PwdUpdateTime = DateTime.Now
        };
        user.OwnerUserId = user.Id;
        var role = new SysRole
        {
            Id = SnowflakeId.NextId(), RoleCode = "r_" + tag, RoleName = "日志夹具角色",
            DataScope = scope, Status = EnableStatus.Enabled, Sort = 999
        };
        await Db.Insertable(user).ExecuteCommandAsync();
        await Db.Insertable(role).ExecuteCommandAsync();
        await Db.Insertable(new SysUserRole { Id = SnowflakeId.NextId(), UserId = user.Id, RoleId = role.Id })
            .ExecuteCommandAsync();
        return user;
    }

    private async Task LoginLogAsync(string userName)
        => await Db.Insertable(new SysLoginLog
        {
            Id = SnowflakeId.NextId(), UserName = userName, Result = "夹具回归", Success = true,
            Ip = "127.0.0.1", CreateTime = DateTime.Now
        }).ExecuteCommandAsync();

    [Fact]
    public async Task Self_Scope_Sees_Only_Own_Login_Logs()
    {
        var tag = Tag();
        var me = await UserAsync(DataScopeType.Self, 3, tag);
        var other = "z_" + tag;
        await LoginLogAsync(me.UserName);
        await LoginLogAsync(other);
        try
        {
            var mine = await Fx.Logs().LoginPageAsync(new LoginLogQuery { PageSize = 200 }, me.Id);
            Assert.Contains(mine.Rows, r => r.UserName == me.UserName);      // 自己的在
            Assert.DoesNotContain(mine.Rows, r => r.UserName == other);      // 别人的不在
            Assert.All(mine.Rows, r => Assert.Equal(me.UserName, r.UserName));

            // 超管（种子 admin=All 档）不受限，仍能看到那条别人的记录
            var super = await Fx.Logs().LoginPageAsync(
                new LoginLogQuery { UserName = other, PageSize = 200 }, 1);
            Assert.Contains(super.Rows, r => r.UserName == other);
        }
        finally
        {
            await CleanAsync(tag, me.UserName, other);
        }
    }

    [Fact]
    public async Task Dept_Scope_Without_Dept_Sees_Nothing()
    {
        var tag = Tag();
        var me = await UserAsync(DataScopeType.Dept, null, tag); // 无部门 + 本部门档 → 空集
        await LoginLogAsync(me.UserName);
        try
        {
            var page = await Fx.Logs().LoginPageAsync(new LoginLogQuery { PageSize = 200 }, me.Id);
            Assert.Empty(page.Rows);
            Assert.Equal(0, page.Total);
        }
        finally
        {
            await CleanAsync(tag, me.UserName, null);
        }
    }

    [Fact]
    public async Task Self_Scope_Filters_Operation_Logs_Too()
    {
        var tag = Tag();
        var me = await UserAsync(DataScopeType.Self, 3, tag);
        var other = "z_" + tag;
        async Task Oper(string userName) => await Db.Insertable(new SysOperationLog
        {
            Id = SnowflakeId.NextId(), Module = "夹具", Action = "Probe", Method = "GET", Url = "/api/v1/probe",
            UserName = userName, Success = true, CreateTime = DateTime.Now
        }).ExecuteCommandAsync();
        await Oper(me.UserName);
        await Oper(other);
        try
        {
            var page = await Fx.Logs().OperPageAsync(
                new OperLogQuery { Module = "夹具", PageSize = 200 }, me.Id);
            Assert.Contains(page.Rows, r => r.UserName == me.UserName);
            Assert.DoesNotContain(page.Rows, r => r.UserName == other);
        }
        finally
        {
            await Db.Deleteable<SysOperationLog>().Where(l => l.Module == "夹具").ExecuteCommandAsync();
            await CleanAsync(tag, me.UserName, null);
        }
    }

    private async Task CleanAsync(string tag, string? meName, string? otherName)
    {
        await Db.Deleteable<SysLoginLog>().Where(l => l.UserName == meName || l.UserName == otherName)
            .ExecuteCommandAsync();
        var userIds = await Db.Queryable<SysUser>().Where(u => u.UserName == "u_" + tag).Select(u => u.Id)
            .ToListAsync();
        if (userIds.Count > 0)
            await Db.Deleteable<SysUserRole>().Where(l => userIds.Contains(l.UserId)).ExecuteCommandAsync();
        await Db.Deleteable<SysUser>().Where(u => u.UserName == "u_" + tag).ExecuteCommandAsync();
        await Db.Deleteable<SysRole>().Where(r => r.RoleCode == "r_" + tag).ExecuteCommandAsync();
    }
}
