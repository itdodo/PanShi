using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panshi.Api.Authorization;
using Panshi.Api.Filters;
using Panshi.Api.Middleware;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Service.Scm;

namespace Panshi.Api.Controllers;

/// <summary>
/// 库存单据。过账与作废单独一个权限码（scm:stockdoc:post）——它们是唯一会改库存的动作，
/// 能录单的人不必有权过账。归属人/部门从令牌取，前端不传。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/scm/stock-doc")]
[Tags("供应链-库存单据")]
public class StockDocController(StockDocService docs) : ApiControllerBase
{
    private string Name => HttpContext.CurrentDisplayName();

    private long? Dept => long.TryParse(User.FindFirst("dept")?.Value, out var d) ? d : null;

    [HttpGet("page")]
    [HasPermission("scm:stockdoc:list")]
    public async Task<PagedResult<StockDocDto>> Page([FromQuery] StockDocQuery query) => await docs.PageAsync(query, Uid);

    [HttpGet("{id:long}")]
    [HasPermission("scm:stockdoc:list")]
    public async Task<StockDocDto> Get(long id) => await docs.GetAsync(id, Uid);

    [HttpPost]
    [HasPermission("scm:stockdoc:add")]
    public Task<StockDocDto> Create([FromBody] StockDocSaveDto dto) => docs.CreateAsync(dto, Uid, Name, Dept);

    [HttpPut("{id:long}")]
    [HasPermission("scm:stockdoc:edit")]
    public async Task Update(long id, [FromBody] StockDocSaveDto dto) => await docs.UpdateAsync(id, dto, Uid);

    [HttpDelete("{id:long}")]
    [HasPermission("scm:stockdoc:delete")]
    public async Task Delete(long id) => await docs.DeleteAsync(id, Uid);

    [HttpPost("{id:long}/post")]
    [HasPermission("scm:stockdoc:post")]
    [NoRepeatSubmit]
    public async Task<StockDocDto> Post(long id) => await docs.PostAsync(id, Uid, Name);

    [HttpPost("{id:long}/void")]
    [HasPermission("scm:stockdoc:post")]
    [NoRepeatSubmit]
    public async Task<StockDocDto> Void(long id) => await docs.VoidAsync(id, Uid, Name);
}

/// <summary>库存台账。</summary>
[ApiController]
[Authorize]
[Route("api/v1/scm/stock")]
[Tags("供应链-库存台账")]
public class StockController(StockService stock) : ApiControllerBase
{
    [HttpGet("page")]
    [HasPermission("scm:stock:list")]
    public async Task<PagedResult<StockDto>> Page([FromQuery] StockQuery query) => await stock.PageAsync(query);
}

/// <summary>库存流水（只读）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/scm/ledger")]
[Tags("供应链-库存流水")]
public class LedgerController(LedgerService ledger) : ApiControllerBase
{
    [HttpGet("page")]
    [HasPermission("scm:ledger:list")]
    public async Task<PagedResult<LedgerDto>> Page([FromQuery] LedgerQuery query) => await ledger.PageAsync(query);
}

/// <summary>库存预警（只读）。阈值挂在物料主数据上，这里只做展开与判档。</summary>
[ApiController]
[Authorize]
[Route("api/v1/scm/stock-alert")]
[Tags("供应链-库存预警")]
public class StockAlertController(StockAlertService alerts) : ApiControllerBase
{
    [HttpGet("page")]
    [HasPermission("scm:alert:list")]
    public async Task<PagedResult<StockAlertDto>> Page([FromQuery] StockAlertQuery query) => await alerts.PageAsync(query);
}

/// <summary>进销存汇总（只读报表）。期初/收入/发出/期末按流水聚合，与库存台账对账用。</summary>
[ApiController]
[Authorize]
[Route("api/v1/scm/stock-summary")]
[Tags("供应链-进销存汇总")]
public class StockSummaryController(StockSummaryService summary) : ApiControllerBase
{
    [HttpGet("page")]
    [HasPermission("scm:summary:list")]
    public async Task<PagedResult<StockSummaryDto>> Page([FromQuery] StockSummaryQuery query) => await summary.PageAsync(query);
}
