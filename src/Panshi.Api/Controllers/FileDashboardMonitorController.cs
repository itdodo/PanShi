using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panshi.Api.Authorization;
using Panshi.Api.Middleware;
using Panshi.Api.Services;
using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Repository;
using Panshi.Service.Biz;
using SqlSugar;

namespace Panshi.Api.Controllers;

/// <summary>文件上传下载（安全清单 #4：白名单+大小+魔数嗅探）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/file")]
[Tags("文件")]
public class FileController(FileStorage storage, IRepository<SysFile> files, FileAccessService access) : ApiControllerBase
{
    public sealed record FileDto(string Id, string Name, long Size, string Url);

    [HttpPost("upload")]
    public async Task<FileDto> Upload(IFormFile file, [FromQuery] string? bizType = null)
    {
        if (file is null || file.Length == 0) throw new BizException("请选择文件");
        var meta = await storage.SaveAsync(file, bizType);
        await files.InsertAsync(meta);
        return new FileDto(meta.Id.ToString(), meta.FileName, meta.Size, $"/api/v1/file/{meta.Id}/download");
    }

    [HttpGet("{id:long}")]
    public async Task<FileDto> Meta(long id)
    {
        await access.EnsureReadableAsync(id, Uid);
        var f = await files.GetAsync(id);
        return new FileDto(f.Id.ToString(), f.FileName, f.Size, $"/api/v1/file/{f.Id}/download");
    }

    [HttpGet("{id:long}/download")]
    public async Task<IActionResult> Download(long id)
    {
        await access.EnsureReadableAsync(id, Uid);
        var f = await files.GetAsync(id);
        var path = storage.ResolvePath(f);
        if (!global::System.IO.File.Exists(path)) throw BizException.NotFound("文件内容");
        return PhysicalFile(path, f.ContentType, f.FileName);
    }
}

/// <summary>仪表盘统计（登录即可）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/dashboard")]
[Tags("仪表盘")]
public class DashboardController(
    ISqlSugarClient db,
    IRepository<SysUserSession> sessions,
    IRepository<SysMessage> messages) : ApiControllerBase
{
    /// <summary>
    /// 首页统计卡。这里刻意**不**按数据权限收敛：四个数都是聚合计数（全库用户数 / 在线数 /
    /// 我的未读 / 我的待办），后两个本就只算当前用户，前两个不暴露任何行级数据。
    /// 真要给受限角色看「本部门人数」，另开带 scope 的端点，别改这里。
    /// </summary>
    [HttpGet("stats")]
    public async Task<object> Stats()
    {
        var userCount = await db.Queryable<SysUser>().CountAsync(it => !it.IsDeleted);
        var onlineCount = (await sessions.ListAsync(s => s.ExpireTime > DateTime.Now)).Select(s => s.UserId).Distinct().Count();
        var unread = await messages.CountAsync(m => m.ReceiverId == Uid && !m.IsRead);
        long todoCount = 0;
        try
        {
            todoCount = await db.Queryable<Model.Entities.SysFlowTask>()
                .CountAsync(t => t.ApproverUserId == Uid && t.Status == Model.Enums.FlowTaskStatus.Pending);
        }
        catch
        {
            /* 审批流批次前表可能未使用 */
        }

        return new { userCount, onlineCount, unread, todoCount };
    }
}

/// <summary>服务监控（运行时指标 + 数据库健康）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/monitor")]
[Tags("监控")]
public class MonitorController(ISqlSugarClient db, RequestMetrics metrics) : ApiControllerBase
{
    /// <summary>
    /// ⚠️ 权限码是种子里早就有的 monitor:server:list——此前这个端点只挂 [Authorize]，
    /// 等于任何登录用户（含没被授「服务监控」菜单的账号）都能读到 PG 版本、库大小与机器名。
    /// </summary>
    [HttpGet("server")]
    [HasPermission("monitor:server:list")]
    public async Task<object> Server()
    {
        var proc = Process.GetCurrentProcess();
        var pgVersion = await db.Ado.GetStringAsync("show server_version");
        var dbSize = await db.Ado.GetStringAsync(
            "select pg_size_pretty(pg_database_size(current_database()))");
        return new
        {
            machineName = Environment.MachineName,
            os = RuntimeInformation.OSDescription,
            framework = RuntimeInformation.FrameworkDescription,
            cpuCores = Environment.ProcessorCount,
            memWorkingSetMb = Math.Round(proc.WorkingSet64 / 1024.0 / 1024.0, 1),
            memHeapMb = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 1),
            uptimeMin = Math.Round((DateTime.Now - proc.StartTime).TotalMinutes, 1),
            startupTime = proc.StartTime,
            pgVersion,
            dbSize
        };
    }

    /// <summary>
    /// 进程内累计量快照：请求状态码分布 + 耗时（自启动累计，无百分位）+ GC/线程池 + 数据库连接占用。
    /// 排障用法：按固定间隔抓两次，差值即速率；`db.activeConnections` 逼近连接池上限时
    /// <c>requests.active</c> 也会同时抬高，那就是在排队而不是在算。
    /// ⚠️ 只给累计量是刻意的——引真方方图/直方桶要加依赖，蓝图「明确不引入」清单挡着。
    /// </summary>
    [HttpGet("metrics")]
    [HasPermission("monitor:server:list")]
    public async Task<object> Metrics()
    {
        var proc = Process.GetCurrentProcess();
        var snap = metrics.Take();
        var gc = GC.GetGCMemoryInfo();
        ThreadPool.GetAvailableThreads(out var workerFree, out var ioFree);
        ThreadPool.GetMaxThreads(out var workerMax, out var ioMax);
        var activeConnections = (await db.Ado.SqlQueryAsync<int>(
            "select count(*)::int from pg_stat_activity where datname = current_database()")).FirstOrDefault();

        return new MetricsDto
        {
            MachineName = Environment.MachineName,
            UptimeMin = Math.Round((DateTime.Now - proc.StartTime).TotalMinutes, 1),
            Requests = new RequestCountersDto
            {
                Total = snap.Total, Active = snap.Active, ServerErrors = snap.ServerErrors,
                ClientErrors = snap.ClientErrors, Unauthorized = snap.Unauthorized, Forbidden = snap.Forbidden,
                Conflict = snap.Conflict, Throttled = snap.Throttled, AvgMs = snap.AvgMs, MaxMs = snap.MaxMs
            },
            Gc = new GcStatsDto
            {
                IsServer = GCSettings.IsServerGC,
                Gen0 = GC.CollectionCount(0), Gen1 = GC.CollectionCount(1), Gen2 = GC.CollectionCount(2),
                HeapMb = Math.Round(GC.GetTotalMemory(false) / 1024.0 / 1024.0, 1),
                CommittedMb = Math.Round(gc.TotalCommittedBytes / 1024.0 / 1024.0, 1),
                PausePercent = Math.Round(gc.PauseTimePercentage, 2)
            },
            Threads = new ThreadStatsDto
            {
                WorkerBusy = workerMax - workerFree, WorkerMax = workerMax,
                IoBusy = ioMax - ioFree, IoMax = ioMax, OsThreads = proc.Threads.Count
            },
            Db = new DbStatsDto { ActiveConnections = activeConnections }
        };
    }
}
