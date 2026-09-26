using MiniExcelLibs;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Repository;
using SqlSugar;

namespace Panshi.Service.Sys;

/// <summary>
/// 日志服务：操作/登录/变更分页查询、导出、清理（保留期天数由作业与手动共用）。
/// ⚠️ 三张日志表都只有 user_name、没有 DeptId/OwnerUserId，走不了 DataScopeService.Filter&lt;T&gt;，
/// 一律按 VisibleUserNamesAsync 收敛（超管/All 档返回 null=不过滤）。
/// </summary>
public class LogService(
    IRepository<SysOperationLog> operRepo,
    IRepository<SysLoginLog> loginRepo,
    IRepository<SysChangeLog> changeRepo,
    Base.DataScopeService dataScope)
{
    /// <summary>
    /// 可见用户名集合转成 IN 条件。空集合换成一个不可能命中的哨兵值，
    /// 而不是拼恒假表达式——无部门用户拿到「本部门」档时就是空集，应当什么都看不到。
    /// </summary>
    private static IReadOnlyList<string> OrNone(List<string> names) =>
        names.Count == 0 ? ["__no_visible_user__"] : names;

    public async Task<PagedResult<OperLogDto>> OperPageAsync(OperLogQuery query, long userId)
    {
        var exp = await BuildOperExpAsync(query, userId);
        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        var page = await operRepo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<OperLogDto> { Total = page.Total, Rows = page.Rows.Select(ToOperDto).ToList() };
    }

    public async Task<byte[]> OperExportBytesAsync(OperLogQuery query, long userId)
    {
        var exp = await BuildOperExpAsync(query, userId);
        var rows = await operRepo.Db.Queryable<SysOperationLog>().Where(exp.ToExpression())
            .OrderBy(it => it.CreateTime, OrderByType.Desc).Take(5000).ToListAsync();
        using var ms = new MemoryStream();
        await ms.SaveAsAsync(rows.Select(ToOperDto));
        return ms.ToArray();
    }

    public async Task<int> OperCleanupAsync(int keepDays)
        => await operRepo.DeleteAsync(l => l.CreateTime < DateTime.Now.AddDays(-keepDays));

    public async Task<PagedResult<LoginLogDto>> LoginPageAsync(LoginLogQuery query, long userId)
    {
        var userName = query.UserName?.Trim();
        var exp = Expressionable.Create<SysLoginLog>();
        if (!string.IsNullOrEmpty(userName)) exp.And(l => l.UserName.Contains(userName));
        if (query.Success is not null) exp.And(l => l.Success == query.Success!.Value);
        if (query.Begin is DateTime b) exp.And(l => l.CreateTime >= b);
        if (query.End is DateTime e) exp.And(l => l.CreateTime <= e.AddDays(1));
        var names = await dataScope.VisibleUserNamesAsync(userId);
        if (names is not null)
        {
            var visible = OrNone(names);
            exp.And(l => visible.Contains(l.UserName));
        }
        var page = await loginRepo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, "create_time");
        return new PagedResult<LoginLogDto>
        {
            Total = page.Total,
            Rows = page.Rows.Select(l => new LoginLogDto
            {
                Id = l.Id.ToString(), UserName = l.UserName, Result = l.Result, Success = l.Success,
                Ip = l.Ip, UserAgent = l.UserAgent, CreateTime = l.CreateTime
            }).ToList()
        };
    }

    public async Task<byte[]> LoginExportBytesAsync(LoginLogQuery query, long userId)
    {
        var result = await LoginPageAsync(new LoginLogQuery
        {
            UserName = query.UserName, Success = query.Success, Begin = query.Begin, End = query.End, PageSize = 200
        }, userId);
        using var ms = new MemoryStream();
        await ms.SaveAsAsync(result.Rows);
        return ms.ToArray();
    }

    public async Task<int> LoginCleanupAsync(int keepDays)
        => await loginRepo.DeleteAsync(l => l.CreateTime < DateTime.Now.AddDays(-keepDays));

    public async Task<PagedResult<ChangeLogDto>> ChangePageAsync(ChangeLogQuery query, long userId)
    {
        var tableName = query.TableName?.Trim();
        var userName = query.UserName?.Trim();
        var exp = Expressionable.Create<SysChangeLog>();
        if (!string.IsNullOrEmpty(tableName)) exp.And(c => c.TableName == tableName);
        if (!string.IsNullOrEmpty(userName)) exp.And(c => c.UserName.Contains(userName));
        if (long.TryParse(query.RecordId, out var rid) && rid > 0) exp.And(c => c.RecordId == rid);
        var names = await dataScope.VisibleUserNamesAsync(userId);
        if (names is not null)
        {
            var visible = OrNone(names);
            exp.And(c => visible.Contains(c.UserName));
        }
        var page = await changeRepo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, "create_time");
        return new PagedResult<ChangeLogDto>
        {
            Total = page.Total,
            Rows = page.Rows.Select(c => new ChangeLogDto
            {
                Id = c.Id.ToString(), TableName = c.TableName, RecordId = c.RecordId.ToString(),
                Changes = c.Changes, UserName = c.UserName, CreateTime = c.CreateTime
            }).ToList()
        };
    }

    public async Task<int> ChangeCleanupAsync(int keepDays)
        => await changeRepo.DeleteAsync(c => c.CreateTime < DateTime.Now.AddDays(-keepDays));

    private async Task<Expressionable<SysOperationLog>> BuildOperExpAsync(OperLogQuery query, long userId)
    {
        var module = query.Module?.Trim();
        var userName = query.UserName?.Trim();
        var exp = Expressionable.Create<SysOperationLog>();
        if (!string.IsNullOrEmpty(module)) exp.And(l => l.Module.Contains(module));
        if (!string.IsNullOrEmpty(userName)) exp.And(l => l.UserName!.Contains(userName));
        if (query.Success is not null) exp.And(l => l.Success == query.Success!.Value);
        if (query.Begin is DateTime b) exp.And(l => l.CreateTime >= b);
        if (query.End is DateTime e) exp.And(l => l.CreateTime <= e.AddDays(1));
        var names = await dataScope.VisibleUserNamesAsync(userId);
        if (names is not null)
        {
            var visible = OrNone(names);
            exp.And(l => l.UserName != null && visible.Contains(l.UserName));
        }
        return exp;
    }

    private static OperLogDto ToOperDto(SysOperationLog l) => new()
    {
        Id = l.Id.ToString(), Module = l.Module, Action = l.Action, Method = l.Method, Url = l.Url,
        Params = l.Params, UserName = l.UserName, Ip = l.Ip, ElapsedMs = l.ElapsedMs,
        Success = l.Success, ErrorMsg = l.ErrorMsg, CreateTime = l.CreateTime
    };
}

/// <summary>在线会话监控（monitor/online：会话+用户昵称 join）。</summary>
public class OnlineService(IRepository<SysUserSession> sessionRepo, IRepository<SysUser> userRepo)
{
    public async Task<List<SessionDto>> ListAsync(string? keyword = null)
    {
        var sessions = await sessionRepo.ListAsync(s => s.ExpireTime > DateTime.Now);
        var users = (await userRepo.ListAsync()).ToDictionary(u => u.Id, u => u.NickName);
        return sessions.OrderByDescending(s => s.CreateTime)
            .Select(s => new SessionDto
            {
                Id = s.Id.ToString(), UserId = s.UserId.ToString(),
                UserName = s.UserName is null ? null : $"{users.GetValueOrDefault(s.UserId, s.UserName)}（{s.UserName}）",
                LoginIp = s.LoginIp, UserAgent = s.UserAgent, CreateTime = s.CreateTime, ExpireTime = s.ExpireTime
            })
            .Where(x => string.IsNullOrWhiteSpace(keyword) ||
                        x.UserName?.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase) == true)
            .ToList();
    }
}
