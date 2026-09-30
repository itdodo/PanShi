using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Repository;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 日志三张列表的时间区间契约：含头含尾，上界不再 +1 天。
/// 变更日志原本压根没有 Begin/End（三个日志页里唯一定不了时间范围的）；登录/操作日志把上界写成
/// End.AddDays(1)，而前端传的 end 已是当天 23:59:59，两头叠加会多框出一整天。
/// 三页各插同样四条时间：-5d / -2d / -36h / -1h。-36h 是哨兵——它落在「End 与 End+1 天」之间，
/// 只有 +1 天的写法才会把它捞进来；行距拉太开（比如只放 -2d 和 -1h）就测不到这条路径。
/// </summary>
[Collection("pg")]
public class LogRangeTests(PgFixture fx) : PgTestBase(fx)
{
    private static string Tag() => "zz" + SnowflakeId.NextId();

    private static DateTime[] ProbeTimes(DateTime now)
        => [now.AddDays(-5), now.AddDays(-2), now.AddHours(-36), now.AddHours(-1)];

    [Fact]
    public async Task Change_Log_Range_Is_Inclusive_And_Not_Widened()
    {
        var table = Tag();
        var now = DateTime.Now;
        var at = ProbeTimes(now);
        foreach (var t in at)
            await Db.Insertable(new SysChangeLog
            {
                Id = SnowflakeId.NextId(), TableName = table, RecordId = 1, Changes = "[]",
                UserId = 1, UserName = "admin", CreateTime = t
            }).ExecuteCommandAsync();
        var svc = Fx.Logs();
        try
        {
            Assert.Equal(4, (await svc.ChangePageAsync(new ChangeLogQuery { TableName = table, PageSize = 50 }, 1)).Total);
            Assert.Equal(3, (await svc.ChangePageAsync(
                new ChangeLogQuery { TableName = table, PageSize = 50, Begin = now.AddDays(-3) }, 1)).Total);

            var to2d = await svc.ChangePageAsync(new ChangeLogQuery
            {
                TableName = table, PageSize = 50, Begin = now.AddDays(-3), End = now.AddDays(-2).AddSeconds(30)
            }, 1);
            Assert.Equal(1, to2d.Total);
            Assert.Equal(at[1].Date, to2d.Rows[0].CreateTime.Date);

            // 区间落在所有行之前 → 空页，total 跟着归零（不是「查到了但没显示」）
            var none = await svc.ChangePageAsync(new ChangeLogQuery
            {
                TableName = table, PageSize = 50, Begin = now.AddYears(-2), End = now.AddYears(-1)
            }, 1);
            Assert.Equal(0, none.Total);
            Assert.Empty(none.Rows);
        }
        finally
        {
            await Db.Deleteable<SysChangeLog>().Where(c => c.TableName == table).ExecuteCommandAsync();
        }
    }

    [Fact]
    public async Task Login_Log_Range_Is_Inclusive_And_Not_Widened()
    {
        var user = Tag();
        var now = DateTime.Now;
        var at = ProbeTimes(now);
        foreach (var t in at)
            await Db.Insertable(new SysLoginLog
            {
                Id = SnowflakeId.NextId(), UserName = user, Result = "区间回归", Success = true,
                Ip = "127.0.0.1", CreateTime = t
            }).ExecuteCommandAsync();
        var svc = Fx.Logs();
        try
        {
            Assert.Equal(4, (await svc.LoginPageAsync(new LoginLogQuery { UserName = user, PageSize = 50 }, 1)).Total);

            var to2d = await svc.LoginPageAsync(new LoginLogQuery
            {
                UserName = user, PageSize = 50, Begin = now.AddDays(-3), End = now.AddDays(-2).AddSeconds(30)
            }, 1);
            Assert.Equal(1, to2d.Total);
            Assert.Equal(at[1].Date, to2d.Rows[0].CreateTime.Date);
        }
        finally
        {
            await Db.Deleteable<SysLoginLog>().Where(l => l.UserName == user).ExecuteCommandAsync();
        }
    }

    [Fact]
    public async Task Operation_Log_Range_Is_Inclusive_And_Not_Widened()
    {
        var module = Tag();
        var now = DateTime.Now;
        var at = ProbeTimes(now);
        foreach (var t in at)
            await Db.Insertable(new SysOperationLog
            {
                Id = SnowflakeId.NextId(), Module = module, Action = "Probe", Method = "GET",
                Url = "/api/v1/probe", UserName = "admin", Success = true, CreateTime = t
            }).ExecuteCommandAsync();
        var svc = Fx.Logs();
        try
        {
            Assert.Equal(4, (await svc.OperPageAsync(new OperLogQuery { Module = module, PageSize = 50 }, 1)).Total);

            var to2d = await svc.OperPageAsync(new OperLogQuery
            {
                Module = module, PageSize = 50, Begin = now.AddDays(-3), End = now.AddDays(-2).AddSeconds(30)
            }, 1);
            Assert.Equal(1, to2d.Total);
            Assert.Equal(at[1].Date, to2d.Rows[0].CreateTime.Date);
        }
        finally
        {
            await Db.Deleteable<SysOperationLog>().Where(l => l.Module == module).ExecuteCommandAsync();
        }
    }
}
