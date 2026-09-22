using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panshi.Api.Authorization;
using Panshi.Api.Middleware;
using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Service.Sys;

namespace Panshi.Api.Controllers.System;

/// <summary>用户管理。</summary>
[ApiController]
[Authorize]
[Route("api/v1/sys/user")]
[Tags("系统-用户")]
public class UserController(UserService users, PermissionService permissions) : ControllerBase
{
    private long Uid => HttpContext.CurrentUserId();

    private async Task<Panshi.Service.Sys.UserAuth> Actor() =>
        await permissions.GetAuthAsync(Uid) ?? throw BizException.Unauthorized();

    [HttpGet("page")]
    [HasPermission("sys:user:list")]
    public async Task<PagedResult<UserDto>> Page([FromQuery] UserQuery query) => await users.PageAsync(query, Uid);

    [HttpGet("options")]
    public async Task<List<OptionDto>> Options() => await users.OptionsAsync();

    [HttpGet("{id:long}")]
    [HasPermission("sys:user:list")]
    public async Task<UserDto> Get(long id) => await users.GetAsync(id);

    [HttpPost]
    [HasPermission("sys:user:add")]
    public async Task<UserDto> Create([FromBody] UserCreateDto dto) => await users.CreateAsync(dto, await Actor());

    [HttpPut("{id:long}")]
    [HasPermission("sys:user:edit")]
    public async Task Update(long id, [FromBody] UserUpdateDto dto) => await users.UpdateAsync(id, dto, await Actor());

    [HttpDelete("{id:long}")]
    [HasPermission("sys:user:delete")]
    public async Task Delete(long id) => await users.DeleteAsync(id, await Actor());

    /// <summary>重置为初始密码（admin 账号拒绝——防接管①）。</summary>
    [HttpPost("{id:long}/password/reset")]
    [HasPermission("sys:user:resetpwd")]
    public async Task<Common.Results.ApiResult<string>> ResetPassword(long id)
        => Common.Results.ApiResult.Ok(await users.ResetPasswordAsync(id), "已重置");

    [HttpPost("{id:long}/roles")]
    [HasPermission("sys:user:edit")]
    public async Task AssignRoles(long id, [FromBody] AssignRolesDto dto) => await users.AssignRolesAsync(id, dto, await Actor());

    [HttpGet("import-template")]
    [HasPermission("sys:user:import")]
    public async Task<IActionResult> Template()
    {
        var bytes = await users.TemplateBytesAsync();
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "用户导入模板.xlsx");
    }

    [HttpPost("import")]
    [HasPermission("sys:user:import")]
    public async Task<ImportResultDto> Import(IFormFile file)
    {
        if (file is null || file.Length == 0) throw new BizException("请选择文件");
        await using var stream = file.OpenReadStream();
        return await users.ImportAsync(stream, await Actor());
    }

    [HttpGet("export")]
    [HasPermission("sys:user:export")]
    public async Task<IActionResult> Export([FromQuery] UserQuery query)
    {
        var bytes = await users.ExportBytesAsync(query, Uid);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "用户.xlsx");
    }
}
