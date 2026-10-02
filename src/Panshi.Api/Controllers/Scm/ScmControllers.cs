using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panshi.Api.Authorization;
using Panshi.Api.Middleware;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Service.Scm;

namespace Panshi.Api.Controllers;

/// <summary>
/// 供应链订单入站编排。归属人/部门从令牌取（与报销、采购申请同一口径），
/// 前端不传 owner，避免伪造归属人绕过数据权限。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/scm/purchase-order")]
[Tags("供应链-采购订单")]
public class PurchaseOrderController(PurchaseOrderService orders) : ApiControllerBase
{
    private string Name => HttpContext.CurrentDisplayName();

    private long? Dept => long.TryParse(User.FindFirst("dept")?.Value, out var d) ? d : null;

    [HttpGet("page")]
    [HasPermission("scm:purchase:list")]
    public async Task<PagedResult<PurchaseOrderDto>> Page([FromQuery] ScmDocQuery query) => await orders.PageAsync(query, Uid);

    [HttpGet("{id:long}")]
    [HasPermission("scm:purchase:list")]
    public async Task<PurchaseOrderDto> Get(long id) => await orders.GetAsync(id, Uid);

    [HttpPost]
    [HasPermission("scm:purchase:add")]
    public Task<PurchaseOrderDto> Create([FromBody] PurchaseOrderSaveDto dto) => orders.CreateAsync(dto, Uid, Name, Dept);

    [HttpPut("{id:long}")]
    [HasPermission("scm:purchase:edit")]
    public async Task Update(long id, [FromBody] PurchaseOrderSaveDto dto) => await orders.UpdateAsync(id, dto, Uid);

    [HttpDelete("{id:long}")]
    [HasPermission("scm:purchase:delete")]
    public async Task Delete(long id) => await orders.DeleteAsync(id, Uid);

    [HttpPost("{id:long}/submit")]
    [HasPermission("scm:purchase:submit")]
    public async Task<PurchaseOrderDto> Submit(long id, [FromBody] FlowSubmitDto payload)
        => await orders.SubmitAsync(id, Uid, Name, payload);
}

/// <summary>销售订单。</summary>
[ApiController]
[Authorize]
[Route("api/v1/scm/sales-order")]
[Tags("供应链-销售订单")]
public class SalesOrderController(SalesOrderService orders) : ApiControllerBase
{
    private string Name => HttpContext.CurrentDisplayName();

    private long? Dept => long.TryParse(User.FindFirst("dept")?.Value, out var d) ? d : null;

    [HttpGet("page")]
    [HasPermission("scm:sales:list")]
    public async Task<PagedResult<SalesOrderDto>> Page([FromQuery] ScmDocQuery query) => await orders.PageAsync(query, Uid);

    [HttpGet("{id:long}")]
    [HasPermission("scm:sales:list")]
    public async Task<SalesOrderDto> Get(long id) => await orders.GetAsync(id, Uid);

    [HttpPost]
    [HasPermission("scm:sales:add")]
    public Task<SalesOrderDto> Create([FromBody] SalesOrderSaveDto dto) => orders.CreateAsync(dto, Uid, Name, Dept);

    [HttpPut("{id:long}")]
    [HasPermission("scm:sales:edit")]
    public async Task Update(long id, [FromBody] SalesOrderSaveDto dto) => await orders.UpdateAsync(id, dto, Uid);

    [HttpDelete("{id:long}")]
    [HasPermission("scm:sales:delete")]
    public async Task Delete(long id) => await orders.DeleteAsync(id, Uid);

    [HttpPost("{id:long}/submit")]
    [HasPermission("scm:sales:submit")]
    public async Task<SalesOrderDto> Submit(long id, [FromBody] FlowSubmitDto payload)
        => await orders.SubmitAsync(id, Uid, Name, payload);
}

/// <summary>供应商绩效（只读报表）。按下单日期区间统计订单量、准交率与平均交付天数。</summary>
[ApiController]
[Authorize]
[Route("api/v1/scm/supplier-performance")]
[Tags("供应链-供应商绩效")]
public class SupplierPerformanceController(SupplierPerformanceService perf) : ApiControllerBase
{
    [HttpGet("page")]
    [HasPermission("scm:supplierperf:list")]
    public async Task<PagedResult<SupplierPerformanceDto>> Page([FromQuery] SupplierPerformanceQuery query)
        => await perf.PageAsync(query);
}

/// <summary>
/// 采购到货计划。计划由采购订单批准后自动生成（一行一条），这里只提供查询与改期——
/// 实际到货量来自已过账的采购入库单，所以没有「登记到货」这个第二入口，也就不会有两本账。
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/scm/arrival")]
[Tags("供应链-到货计划")]
public class ArrivalController(ArrivalService arrivals) : ApiControllerBase
{
    [HttpGet("page")]
    [HasPermission("scm:arrival:list")]
    public async Task<PagedResult<ArrivalDto>> Page([FromQuery] ArrivalQuery query) => await arrivals.PageAsync(query, Uid);

    [HttpPut("{id:long}/reschedule")]
    [HasPermission("scm:arrival:edit")]
    public async Task Reschedule(long id, [FromBody] ArrivalRescheduleDto dto) => await arrivals.RescheduleAsync(id, dto);
}
