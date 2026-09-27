using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panshi.Api.Middleware;
using Panshi.Api.Services;
using Panshi.Common.Exceptions;
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
public class FileController(FileStorage storage, IRepository<SysFile> files, FileAccessService access) : ControllerBase
{
    private long Uid => HttpContext.CurrentUserId();

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
    IRepository<SysMessage> messages) : ControllerBase
{
    private long Uid => HttpContext.CurrentUserId();

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
public class MonitorController(ISqlSugarClient db) : ControllerBase
{
    [HttpGet("server")]
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
}
