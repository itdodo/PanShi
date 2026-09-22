using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panshi.Api.Authorization;
using Panshi.Api.Middleware;
using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Service.Sys;

namespace Panshi.Api.Controllers.System;

/// <summary>角色管理。</summary>
[ApiController]
[Authorize]
[Route("api/v1/sys/role")]
[Tags("系统-角色")]
public class RoleController(RoleService roles, PermissionService permissions) : ControllerBase
{
    private long Uid => HttpContext.CurrentUserId();

    private async Task<UserAuth> Actor() => await permissions.GetAuthAsync(Uid) ?? throw BizException.Unauthorized();

    [HttpGet("page")]
    [HasPermission("sys:role:list")]
    public async Task<PagedResult<RoleDto>> Page([FromQuery] RoleQuery query) => await roles.PageAsync(query, Uid);

    [HttpGet("list")]
    public async Task<List<OptionDto>> List() => await roles.ListAsync();

    [HttpGet("{id:long}")]
    [HasPermission("sys:role:list")]
    public async Task<RoleDto> Get(long id) => await roles.GetAsync(id);

    [HttpPost]
    [HasPermission("sys:role:add")]
    public async Task<RoleDto> Create([FromBody] RoleSaveDto dto) => await roles.CreateAsync(dto);

    [HttpPut("{id:long}")]
    [HasPermission("sys:role:edit")]
    public async Task Update(long id, [FromBody] RoleUpdateDto dto) => await roles.UpdateAsync(id, dto, await Actor());

    [HttpDelete("{id:long}")]
    [HasPermission("sys:role:delete")]
    public async Task Delete(long id) => await roles.DeleteAsync(id, await Actor());

    [HttpPost("{id:long}/menus")]
    [HasPermission("sys:role:edit")]
    public async Task GrantMenus(long id, [FromBody] GrantMenusDto dto) => await roles.GrantMenusAsync(id, dto, await Actor());

    [HttpGet("export")]
    [HasPermission("sys:role:list")]
    public async Task<IActionResult> Export()
        => File(await roles.ExportBytesAsync(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "角色.xlsx");
}

/// <summary>菜单管理（tree 全量分权 / tree/my 登录即可）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/sys/menu")]
[Tags("系统-菜单")]
public class MenuController(MenuService menus) : ControllerBase
{
    private long Uid => HttpContext.CurrentUserId();

    [HttpGet("tree")]
    [HasPermission("sys:menu:list")]
    public async Task<List<MenuDto>> Tree() => await menus.FullTreeAsync();

    /// <summary>我的菜单树（动态路由数据源；无角色=空树）。</summary>
    [HttpGet("tree/my")]
    public async Task<List<MenuDto>> MyTree() => await menus.MyTreeAsync(Uid);

    [HttpGet("options")]
    [HasPermission("sys:menu:list")]
    public async Task<List<MenuOptionDto>> Options() => await menus.OptionsAsync();

    [HttpPost]
    [HasPermission("sys:menu:add")]
    public async Task<MenuDto> Create([FromBody] MenuSaveDto dto) => await menus.CreateAsync(dto);

    [HttpPut("{id:long}")]
    [HasPermission("sys:menu:edit")]
    public async Task Update(long id, [FromBody] MenuSaveDto dto) => await menus.UpdateAsync(id, dto);

    [HttpDelete("{id:long}")]
    [HasPermission("sys:menu:delete")]
    public async Task Delete(long id) => await menus.DeleteAsync(id);
}

/// <summary>部门管理。</summary>
[ApiController]
[Authorize]
[Route("api/v1/sys/dept")]
[Tags("系统-部门")]
public class DeptController(DeptService depts) : ControllerBase
{
    [HttpGet("tree")]
    public async Task<List<DeptDto>> Tree([FromQuery] string? keyword) => await depts.TreeAsync(keyword);

    [HttpGet("options")]
    public async Task<List<OptionDto>> Options() => await depts.OptionsAsync();

    [HttpPost]
    [HasPermission("sys:dept:add")]
    public async Task<DeptDto> Create([FromBody] DeptSaveDto dto) => await depts.CreateAsync(dto);

    [HttpPut("{id:long}")]
    [HasPermission("sys:dept:edit")]
    public async Task Update(long id, [FromBody] DeptSaveDto dto) => await depts.UpdateAsync(id, dto);

    [HttpDelete("{id:long}")]
    [HasPermission("sys:dept:delete")]
    public async Task Delete(long id) => await depts.DeleteAsync(id);
}

/// <summary>岗位管理。</summary>
[ApiController]
[Authorize]
[Route("api/v1/sys/position")]
[Tags("系统-岗位")]
public class PositionController(PositionService positions) : ControllerBase
{
    [HttpGet("page")]
    [HasPermission("sys:position:list")]
    public async Task<PagedResult<PositionDto>> Page([FromQuery] PositionQuery query) => await positions.PageAsync(query);

    [HttpGet("list")]
    public async Task<List<OptionDto>> List() => await positions.ListAsync();

    [HttpPost]
    [HasPermission("sys:position:add")]
    public async Task<PositionDto> Create([FromBody] PositionSaveDto dto) => await positions.CreateAsync(dto);

    [HttpPut("{id:long}")]
    [HasPermission("sys:position:edit")]
    public async Task Update(long id, [FromBody] PositionSaveDto dto) => await positions.UpdateAsync(id, dto);

    [HttpDelete("{id:long}")]
    [HasPermission("sys:position:delete")]
    public async Task Delete(long id) => await positions.DeleteAsync(id);
}
