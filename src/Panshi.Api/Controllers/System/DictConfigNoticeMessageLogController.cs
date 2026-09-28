using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panshi.Api.Authorization;
using Panshi.Api.Middleware;
using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Repository;
using Panshi.Service.Sys;

namespace Panshi.Api.Controllers.System;

/// <summary>字典管理（by-code 登录即可，供前端下拉）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/sys/dict")]
[Tags("系统-字典")]
public class DictController(DictService dict) : ControllerBase
{
    [HttpGet("type/page")]
    [HasPermission("sys:dict:list")]
    public async Task<PagedResult<DictTypeDto>> TypePage([FromQuery] DictTypeQuery query) => await dict.TypePageAsync(query);

    [HttpGet("type/list")]
    public async Task<List<DictTypeDto>> TypeList() => await dict.TypeListAsync();

    [HttpGet("data/{code}")]
    public async Task<List<DictDataDto>> ByCode(string code) => await dict.ByCodeAsync(code);

    [HttpGet("data/type/{typeId:long}")]
    [HasPermission("sys:dict:list")]
    public async Task<List<DictDataDto>> DataList(long typeId) => await dict.DataListAsync(typeId);

    [HttpPost("type")]
    [HasPermission("sys:dict:add")]
    public async Task<DictTypeDto> CreateType([FromBody] DictTypeSaveDto dto) => await dict.CreateTypeAsync(dto);

    [HttpPut("type/{id:long}")]
    [HasPermission("sys:dict:edit")]
    public async Task UpdateType(long id, [FromBody] DictTypeSaveDto dto) => await dict.UpdateTypeAsync(id, dto);

    [HttpDelete("type/{id:long}")]
    [HasPermission("sys:dict:delete")]
    public async Task DeleteType(long id) => await dict.DeleteTypeAsync(id);

    [HttpPost("data")]
    [HasPermission("sys:dict:add")]
    public async Task<DictDataDto> CreateData([FromBody] DictDataSaveDto dto) => await dict.CreateDataAsync(dto);

    [HttpPut("data/{id:long}")]
    [HasPermission("sys:dict:edit")]
    public async Task UpdateData(long id, [FromBody] DictDataSaveDto dto) => await dict.UpdateDataAsync(id, dto);

    [HttpDelete("data/{id:long}")]
    [HasPermission("sys:dict:delete")]
    public async Task DeleteData(long id) => await dict.DeleteDataAsync(id);
}

/// <summary>参数管理。</summary>
[ApiController]
[Authorize]
[Route("api/v1/sys/config")]
[Tags("系统-参数")]
public class ConfigController(ConfigAdminService configs) : ControllerBase
{
    [HttpGet("page")]
    [HasPermission("sys:config:list")]
    public async Task<PagedResult<ConfigDto>> Page([FromQuery] ConfigQuery query) => await configs.PageAsync(query);

    [HttpPost]
    [HasPermission("sys:config:add")]
    public async Task<ConfigDto> Create([FromBody] ConfigSaveDto dto) => await configs.CreateAsync(dto);

    [HttpPut("{id:long}")]
    [HasPermission("sys:config:edit")]
    public async Task Update(long id, [FromBody] ConfigSaveDto dto) => await configs.UpdateAsync(id, dto);

    [HttpDelete("{id:long}")]
    [HasPermission("sys:config:delete")]
    public async Task Delete(long id) => await configs.DeleteAsync(id);
}

/// <summary>公告管理（latest 登录即可）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/sys/notice")]
[Tags("系统-公告")]
public class NoticeController(NoticeService notices) : ControllerBase
{
    [HttpGet("page")]
    [HasPermission("sys:notice:list")]
    public async Task<PagedResult<NoticeDto>> Page([FromQuery] NoticeQuery query) => await notices.PageAsync(query);

    [HttpGet("latest")]
    public async Task<List<NoticeDto>> Latest() => await notices.LatestAsync();

    [HttpGet("{id:long}")]
    public async Task<NoticeDto> Get(long id) => await notices.GetAsync(id);

    [HttpPost]
    [HasPermission("sys:notice:add")]
    public async Task<NoticeDto> Create([FromBody] NoticeSaveDto dto) => await notices.CreateAsync(dto);

    [HttpPut("{id:long}")]
    [HasPermission("sys:notice:edit")]
    public async Task Update(long id, [FromBody] NoticeSaveDto dto) => await notices.UpdateAsync(id, dto);

    [HttpDelete("{id:long}")]
    [HasPermission("sys:notice:delete")]
    public async Task Delete(long id) => await notices.DeleteAsync(id);
}

/// <summary>站内信（send 分权；my 系列登录即可）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/sys/message")]
[Tags("系统-站内信")]
public class MessageController(MessageService messages) : ControllerBase
{
    private long Uid => HttpContext.CurrentUserId();

    [HttpPost("send")]
    [HasPermission("sys:message:send")]
    public async Task Send([FromBody] MessageSendDto dto) =>
        await messages.SendAsync(dto, Uid, HttpContext.CurrentDisplayName());

    [HttpGet("my/page")]
    public async Task<PagedResult<MessageDto>> MyPage([FromQuery] MessageQuery query) =>
        await messages.MyPageAsync(Uid, query);

    [HttpGet("my/unread-count")]
    public async Task<int> UnreadCount() => await messages.UnreadCountAsync(Uid);

    [HttpPost("my/{id:long}/read")]
    public async Task MarkRead(long id) => await messages.MarkReadAsync(Uid, id);

    [HttpPost("read-all")]
    public async Task ReadAll() => await messages.MarkAllReadAsync(Uid);

    /// <summary>删除本人某条消息（软删；不存在/已删按幂等成功）。</summary>
    [HttpDelete("my/{id:long}")]
    public async Task DeleteMy(long id) => await messages.DeleteAsync(Uid, id);

    /// <summary>清空本人已读消息，返回删除条数。⚠️ 与 my/{id:long}/read 段数不同，不会抢匹配。</summary>
    [HttpDelete("my/read")]
    public async Task<int> ClearRead() => await messages.ClearReadAsync(Uid);
}

/// <summary>日志审计。</summary>
[ApiController]
[Authorize]
[Route("api/v1/sys/log")]
[Tags("系统-日志")]
public class LogController(LogService logs) : ControllerBase
{
    /// <summary>日志三表都没有部门/归属列，数据权限一律按「当前查看者」解析（超管=不过滤）。</summary>
    private long Uid => HttpContext.CurrentUserId();

    [HttpGet("operation")]
    [HasPermission("monitor:operlog:list")]
    public async Task<PagedResult<OperLogDto>> Operation([FromQuery] OperLogQuery query) => await logs.OperPageAsync(query, Uid);

    [HttpGet("operation/export")]
    [HasPermission("monitor:operlog:export")]
    public async Task<IActionResult> OperationExport([FromQuery] OperLogQuery query)
        => File(await logs.OperExportBytesAsync(query, Uid), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "操作日志.xlsx");

    [HttpDelete("operation/cleanup")]
    [HasPermission("monitor:operlog:clean")]
    public async Task<int> OperationCleanup([FromQuery] int days = 90) => await logs.OperCleanupAsync(Math.Max(days, 7));

    [HttpGet("login")]
    [HasPermission("monitor:loginlog:list")]
    public async Task<PagedResult<LoginLogDto>> Login([FromQuery] LoginLogQuery query) => await logs.LoginPageAsync(query, Uid);

    [HttpGet("login/export")]
    [HasPermission("monitor:loginlog:export")]
    public async Task<IActionResult> LoginExport([FromQuery] LoginLogQuery query)
        => File(await logs.LoginExportBytesAsync(query, Uid), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "登录日志.xlsx");

    [HttpDelete("login/cleanup")]
    [HasPermission("monitor:loginlog:clean")]
    public async Task<int> LoginCleanup([FromQuery] int days = 90) => await logs.LoginCleanupAsync(Math.Max(days, 7));

    [HttpGet("change")]
    [HasPermission("monitor:changelog:list")]
    public async Task<PagedResult<ChangeLogDto>> Change([FromQuery] ChangeLogQuery query) => await logs.ChangePageAsync(query, Uid);

    [HttpDelete("change/cleanup")]
    [HasPermission("monitor:changelog:clean")]
    public async Task<int> ChangeCleanup([FromQuery] int days = 90) => await logs.ChangeCleanupAsync(Math.Max(days, 7));
}
