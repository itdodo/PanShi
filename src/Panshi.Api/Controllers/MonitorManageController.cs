using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panshi.Api.Authorization;
using Panshi.Api.Jobs;
using Panshi.Api.Middleware;
using Panshi.Model.Dtos;
using Panshi.Service.Sys;

namespace Panshi.Api.Controllers;

/// <summary>运行监控：定时任务管理 + 在线用户强退。</summary>
[ApiController]
[Authorize]
[Route("api/v1/monitor")]
[Tags("监控-任务与在线")]
public class MonitorManageController(JobManagementService jobs, OnlineService online, AuthService auth) : ApiControllerBase
{
    [HttpGet("jobs")]
    [HasPermission("monitor:job:list")]
    public List<object> Jobs() => jobs.List();

    [HttpPost("jobs/{jobId}/trigger")]
    [HasPermission("monitor:job:manage")]
    public void Trigger(string jobId) => jobs.Trigger(jobId);

    [HttpPost("jobs/{jobId}/toggle")]
    [HasPermission("monitor:job:manage")]
    public void Toggle(string jobId, [FromQuery] bool enable) => jobs.Toggle(jobId, enable);

    [HttpGet("online")]
    [HasPermission("monitor:online:list")]
    public async Task<List<SessionDto>> Online([FromQuery] string? keyword) => await online.ListAsync(keyword);

    /// <summary>强退任意会话（monitor:online:kick）。</summary>
    [HttpDelete("online/{sessionId:long}")]
    [HasPermission("monitor:online:kick")]
    public async Task Kick(long sessionId) => await auth.KickSessionByAdminAsync(sessionId);
}
