using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panshi.Api.Authorization;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Service.Sys;

namespace Panshi.Api.Controllers;

/// <summary>
/// IP 黑白名单管理（安全 P1）。判定与防自锁安全栏都在 IpGuardService，这里只做入站编排。
/// 写操作全部落操作日志（AOP 已挂），所以「谁封了谁」可追责。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/monitor/ip-rule")]
[Tags("监控-IP 名单")]
public class IpGuardController(IpGuardService guard) : ApiControllerBase
{
    [HttpGet("page")]
    [HasPermission("monitor:ipguard:list")]
    public async Task<PagedResult<IpRuleDto>> Page([FromQuery] IpRuleQuery query) => await guard.PageAsync(query);

    /// <summary>当前生效形态。封禁前先看这里：DryRun 开着时你的规则其实没在拦人。</summary>
    [HttpGet("status")]
    [HasPermission("monitor:ipguard:list")]
    public object Status() => new { enabled = guard.Enabled, dryRun = guard.DryRun };

    [HttpPost]
    [HasPermission("monitor:ipguard:manage")]
    public async Task<IpRuleDto> Create([FromBody] IpRuleSaveDto dto) => await guard.CreateAsync(dto);

    [HttpPut("{id:long}")]
    [HasPermission("monitor:ipguard:manage")]
    public async Task Update(long id, [FromBody] IpRuleSaveDto dto) => await guard.UpdateAsync(id, dto);

    [HttpDelete("{id:long}")]
    [HasPermission("monitor:ipguard:manage")]
    public async Task Delete(long id) => await guard.DeleteAsync(id);
}
