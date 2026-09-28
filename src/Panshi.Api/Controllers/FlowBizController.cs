using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Panshi.Api.Authorization;
using Panshi.Api.Middleware;
using Panshi.Common.Results;
using Panshi.Service.Biz;
using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Service.Sys;

namespace Panshi.Api.Controllers;

/// <summary>审批流（定义/绑定/任务/实例，蓝图§六 /sys/flow）。</summary>
[ApiController]
[Authorize]
[Route("api/v1/sys/flow")]
[Tags("审批流")]
public class FlowController(
    FlowAdminService admin,
    FlowEngineService engine,
    FlowQueryService query) : ControllerBase
{
    private long Uid => HttpContext.CurrentUserId();

    private string Name => HttpContext.CurrentDisplayName();

    private long? Dept => long.TryParse(User.FindFirst("dept")?.Value, out var d) ? d : null;

    // ---------------- 定义 ----------------
    [HttpGet("def/page")]
    [HasPermission("workflow:def:list")]
    public async Task<PagedResult<FlowDefDto>> DefPage([FromQuery] FlowDefQuery q) => await admin.DefPageAsync(q);

    [HttpGet("def/categories")]
    [HasPermission("workflow:def:list")]
    public async Task<List<string>> Categories() => await admin.CategoriesAsync();

    [HttpGet("def/versions/{code}")]
    [HasPermission("workflow:def:list")]
    public async Task<List<FlowDefDto>> Versions(string code) => await admin.VersionsAsync(code);

    [HttpPost("def")]
    [HasPermission("workflow:def:add")]
    public async Task<FlowDefDto> CreateDef([FromBody] FlowDefSaveDto dto) => await admin.CreateAsync(dto);

    [HttpPut("def/{id:long}")]
    [HasPermission("workflow:def:edit")]
    public async Task<FlowDefDto> UpdateDef(long id, [FromBody] FlowDefSaveDto dto) => await admin.UpdateAsync(id, dto);

    [HttpPost("def/{id:long}/enable")]
    [HasPermission("workflow:def:enable")]
    public async Task EnableDef(long id, [FromBody] FlowDefEnableDto dto) => await admin.EnableAsync(id, dto.Status);

    [HttpDelete("def/{id:long}")]
    [HasPermission("workflow:def:delete")]
    public async Task DeleteDef(long id) => await admin.DeleteAsync(id);

    // ---------------- 绑定 ----------------
    [HttpGet("binding")]
    [HasPermission("workflow:binding:list")]
    public async Task<List<FlowBindingDto>> Bindings() => await admin.BindingsAsync();

    [HttpPut("binding")]
    [HasPermission("workflow:binding:edit")]
    public async Task SaveBinding([FromBody] FlowBindingSaveDto dto) => await admin.SaveBindingAsync(dto);

    [HttpDelete("binding/{id:long}")]
    [HasPermission("workflow:binding:edit")]
    public async Task DeleteBinding(long id) => await admin.DeleteBindingAsync(id);

    // ---------------- 任务 ----------------
    [HttpGet("task/todo")]
    public async Task<PagedResult<FlowTaskDto>> Todo([FromQuery] FlowTaskQuery q) => await query.TodoPageAsync(Uid, q);

    [HttpGet("task/todo-count")]
    public async Task<int> TodoCount() => await query.TodoCountAsync(Uid);

    [HttpGet("task/done")]
    public async Task<PagedResult<FlowTaskDto>> Done([FromQuery] FlowTaskQuery q) => await query.DonePageAsync(Uid, q);

    [HttpPost("task/{id:long}/act")]
    public async Task Act(long id, [FromBody] FlowActDto dto) => await engine.HandleAsync(id, dto, Uid, Name);

    [HttpPost("task/{id:long}/return")]
    public async Task Return(long id, [FromBody] FlowReturnDto dto) => await engine.ReturnAsync(id, dto, Uid, Name);

    [HttpPost("task/{id:long}/transfer")]
    public async Task Transfer(long id, [FromBody] FlowTransferDto dto) => await engine.TransferAsync(id, dto, Uid, Name);

    [HttpPost("task/{id:long}/addsign")]
    public async Task AddSign(long id, [FromBody] FlowAddSignDto dto) => await engine.AddSignAsync(id, dto, Uid, Name);

    // ---------------- 实例 ----------------
    [HttpPost("submit")]
    public async Task<ApiResult<string>> Submit([FromBody] FlowSubmitDto dto)
    {
        var id = await engine.SubmitAsync(dto, Uid, Name);
        return ApiResult.Ok(id == -1 ? "" : id.ToString(), id == -1 ? "未绑定流程，已直通" : "已提交");
    }

    [HttpGet("instance/page")]
    [HasPermission("workflow:instance:list")]
    public async Task<PagedResult<FlowInstanceDto>> InstancePage([FromQuery] FlowInstanceQuery q)
        => await query.InstancePageAsync(q);

    [HttpGet("instance/my")]
    public async Task<PagedResult<FlowInstanceDto>> MyInstances([FromQuery] FlowInstanceQuery q)
        => await query.InstancePageAsync(q, Uid);

    [HttpGet("instance/{id:long}")]
    public async Task<FlowInstanceDetailDto> Detail(long id) => await query.DetailAsync(id, Uid);

    [HttpGet("instance/by-business/{table}/{businessId:long}")]
    public async Task<FlowInstanceDto?> ByBusiness(string table, long businessId)
        => await query.ByBusinessAsync(table, businessId, Uid);

    [HttpPost("instance/{id:long}/withdraw")]
    public async Task Withdraw(long id) => await engine.WithdrawAsync(id, Uid);

    [HttpPost("instance/{id:long}/void")]
    [HasPermission("workflow:instance:void")]
    public async Task VoidInstance(long id, [FromQuery] string? reason)
        => await engine.VoidAsync(id, Uid, string.IsNullOrWhiteSpace(reason) ? "管理员作废" : reason);

    [HttpGet("cc-me")]
    public async Task<PagedResult<FlowCcDto>> CcMe([FromQuery] FlowTaskQuery q) => await query.CcMePageAsync(Uid, q);

    [HttpPost("cc-me/{id:long}/read")]
    public async Task MarkCcRead(long id) => await query.MarkCcReadAsync(Uid, id);
}

/// <summary>业务样板：报销单/采购申请单。</summary>
[ApiController]
[Authorize]
[Route("api/v1/biz")]
[Tags("业务样板")]
public class BizController(ExpenseService expenses, PurchaseService purchases) : ControllerBase
{
    private long Uid => HttpContext.CurrentUserId();

    private string Name => HttpContext.CurrentDisplayName();

    private long? Dept => long.TryParse(User.FindFirst("dept")?.Value, out var d) ? d : null;

    [HttpGet("expense/page")]
    [HasPermission("biz:expense:list")]
    public async Task<PagedResult<ExpenseDto>> ExpensePage([FromQuery] BizDocQuery q) => await expenses.PageAsync(q, Uid);

    [HttpGet("expense/{id:long}")]
    [HasPermission("biz:expense:list")]
    public async Task<ExpenseDto> ExpenseGet(long id) => await expenses.GetAsync(id, Uid);

    [HttpPost("expense")]
    [HasPermission("biz:expense:add")]
    public Task<ExpenseDto> ExpenseCreate([FromBody] ExpenseSaveDto dto)
        => expenses.CreateAsync(dto, Uid, Name, Dept);

    [HttpPut("expense/{id:long}")]
    [HasPermission("biz:expense:edit")]
    public async Task ExpenseUpdate(long id, [FromBody] ExpenseSaveDto dto) => await expenses.UpdateAsync(id, dto, Uid);

    [HttpDelete("expense/{id:long}")]
    [HasPermission("biz:expense:delete")]
    public async Task ExpenseDelete(long id) => await expenses.DeleteAsync(id, Uid);

    [HttpPost("expense/{id:long}/submit")]
    [HasPermission("biz:expense:submit")]
    public async Task<ExpenseDto> ExpenseSubmit(long id, [FromBody] FlowSubmitDto payload)
        => await expenses.SubmitAsync(id, Uid, Name, payload);

    [HttpGet("purchase/page")]
    [HasPermission("biz:purchase:list")]
    public async Task<PagedResult<PurchaseDto>> PurchasePage([FromQuery] BizDocQuery q) => await purchases.PageAsync(q, Uid);

    [HttpGet("purchase/{id:long}")]
    [HasPermission("biz:purchase:list")]
    public async Task<PurchaseDto> PurchaseGet(long id) => await purchases.GetAsync(id, Uid);

    [HttpPost("purchase")]
    [HasPermission("biz:purchase:add")]
    public Task<PurchaseDto> PurchaseCreate([FromBody] PurchaseSaveDto dto)
        => purchases.CreateAsync(dto, Uid, Name, Dept);

    [HttpPut("purchase/{id:long}")]
    [HasPermission("biz:purchase:edit")]
    public async Task PurchaseUpdate(long id, [FromBody] PurchaseSaveDto dto) => await purchases.UpdateAsync(id, dto, Uid);

    [HttpDelete("purchase/{id:long}")]
    [HasPermission("biz:purchase:delete")]
    public async Task PurchaseDelete(long id) => await purchases.DeleteAsync(id, Uid);

    [HttpPost("purchase/{id:long}/submit")]
    [HasPermission("biz:purchase:submit")]
    public async Task<PurchaseDto> PurchaseSubmit(long id, [FromBody] FlowSubmitDto payload)
        => await purchases.SubmitAsync(id, Uid, Name, payload);
}
