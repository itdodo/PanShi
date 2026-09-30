using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panshi.Api.Authorization;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Service.Md;

namespace Panshi.Api.Controllers;

/// <summary>
/// 基础资料三张表的入站编排。options 端点刻意只要登录、不校权限码——
/// 与 /sys/position/list 同一口径：业务单据页要能选到料号/供应商/客户，
/// 而提单的人未必有基础资料的维护权限。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/md/material")]
[Tags("基础资料-物料")]
public class MaterialController(MaterialService materials) : ApiControllerBase
{
    [HttpGet("page")]
    [HasPermission("basedata:material:list")]
    public async Task<PagedResult<MaterialDto>> Page([FromQuery] MaterialQuery query) => await materials.PageAsync(query);

    [HttpGet("options")]
    public async Task<List<OptionDto>> Options() => await materials.OptionsAsync();

    [HttpPost]
    [HasPermission("basedata:material:add")]
    public async Task<MaterialDto> Create([FromBody] MaterialSaveDto dto) => await materials.CreateAsync(dto);

    [HttpPut("{id:long}")]
    [HasPermission("basedata:material:edit")]
    public async Task Update(long id, [FromBody] MaterialSaveDto dto) => await materials.UpdateAsync(id, dto);

    [HttpDelete("{id:long}")]
    [HasPermission("basedata:material:delete")]
    public async Task Delete(long id) => await materials.DeleteAsync(id);
}

/// <summary>供应商管理。</summary>
[ApiController]
[Authorize]
[Route("api/v1/md/supplier")]
[Tags("基础资料-供应商")]
public class SupplierController(SupplierService suppliers) : ApiControllerBase
{
    [HttpGet("page")]
    [HasPermission("basedata:supplier:list")]
    public async Task<PagedResult<SupplierDto>> Page([FromQuery] SupplierQuery query) => await suppliers.PageAsync(query);

    [HttpGet("options")]
    public async Task<List<OptionDto>> Options() => await suppliers.OptionsAsync();

    [HttpPost]
    [HasPermission("basedata:supplier:add")]
    public async Task<SupplierDto> Create([FromBody] SupplierSaveDto dto) => await suppliers.CreateAsync(dto);

    [HttpPut("{id:long}")]
    [HasPermission("basedata:supplier:edit")]
    public async Task Update(long id, [FromBody] SupplierSaveDto dto) => await suppliers.UpdateAsync(id, dto);

    [HttpDelete("{id:long}")]
    [HasPermission("basedata:supplier:delete")]
    public async Task Delete(long id) => await suppliers.DeleteAsync(id);
}

/// <summary>客户管理。</summary>
[ApiController]
[Authorize]
[Route("api/v1/md/customer")]
[Tags("基础资料-客户")]
public class CustomerController(CustomerService customers) : ApiControllerBase
{
    [HttpGet("page")]
    [HasPermission("basedata:customer:list")]
    public async Task<PagedResult<CustomerDto>> Page([FromQuery] CustomerQuery query) => await customers.PageAsync(query);

    [HttpGet("options")]
    public async Task<List<OptionDto>> Options() => await customers.OptionsAsync();

    [HttpPost]
    [HasPermission("basedata:customer:add")]
    public async Task<CustomerDto> Create([FromBody] CustomerSaveDto dto) => await customers.CreateAsync(dto);

    [HttpPut("{id:long}")]
    [HasPermission("basedata:customer:edit")]
    public async Task Update(long id, [FromBody] CustomerSaveDto dto) => await customers.UpdateAsync(id, dto);

    [HttpDelete("{id:long}")]
    [HasPermission("basedata:customer:delete")]
    public async Task Delete(long id) => await customers.DeleteAsync(id);
}
