using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using Panshi.Service.Flow;
using Panshi.Service.Sys;
using SqlSugar;

namespace Panshi.Service.Biz;

/// <summary>
/// 报销单（业务样板：展示「业务接入审批流」标准模式）——
/// ① 实体带 Status/InstanceId；② 提交走 FlowEngineService.SubmitAsync；
/// ③ IFlowBusinessHandler.GetSummaryAsync/OnFinishedAsync 双回调；④ 未绑定流程=直通。
/// </summary>
public class ExpenseService(
    IRepository<BizExpense> repo,
    FlowEngineService engine,
    DataScopeService dataScope) : BaseService<BizExpense>(repo)
{
    public const string Table = "biz_expense";

    public async Task<PagedResult<ExpenseDto>> PageAsync(BizDocQuery query, long userId)
    {
        var kw = query.Keyword?.Trim();
        var ctx = query.Mine ? null : await dataScope.ResolveAsync(userId);
        var exp = Expressionable.Create<BizExpense>();
        if (query.Mine) exp.And(e => e.OwnerUserId == userId);
        else exp.And(DataScopeService.Filter<BizExpense>(ctx));
        if (!string.IsNullOrEmpty(kw)) exp.And(e => e.DocNo.Contains(kw) || e.Reason!.Contains(kw));
        if (query.Status is not null) exp.And(e => e.Status == query.Status!.Value);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<ExpenseDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    /// <summary>详情必须与列表同一套数据权限，否则「仅本人」的人按 id 就能读到别人的单据。</summary>
    public async Task<ExpenseDto> GetAsync(long id, long userId)
    {
        var doc = await Repo.GetAsync(id);
        if (!DataScopeService.IsVisible(await dataScope.ResolveAsync(userId), doc))
            throw BizException.Forbidden("无权查看该单据");
        return ToDto(doc);
    }

    public async Task<ExpenseDto> CreateAsync(ExpenseSaveDto dto, long userId, string userName, long? deptId)
    {
        var doc = new BizExpense
        {
            DocNo = await NextDocNoAsync(), OwnerUserId = userId, OwnerUserName = userName, DeptId = deptId,
            Amount = dto.Amount, Category = dto.Category, Reason = dto.Reason,
            AttachmentIds = SerializeIds(dto.AttachmentIds), Status = BizDocStatus.Draft
        };
        await Repo.InsertAsync(doc);
        return ToDto(doc);
    }

    public async Task UpdateAsync(long id, ExpenseSaveDto dto, long userId)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        doc.Amount = dto.Amount;
        doc.Category = dto.Category;
        doc.Reason = dto.Reason;
        doc.AttachmentIds = SerializeIds(dto.AttachmentIds);
        doc.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(doc);
    }

    public async Task DeleteAsync(long id, long userId)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        await Repo.SoftDeleteAsync(id);
    }

    /// <summary>提交审批（草稿/拒绝/撤回后可重提=新实例从头走）。</summary>
    public async Task<ExpenseDto> SubmitAsync(long id, long userId, string userName, FlowSubmitDto payload)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        if (doc.Status == BizDocStatus.Running) throw new BizException("已在审批中");

        payload.BusinessTable = Table;
        payload.BusinessId = doc.Id;
        payload.Variables["amount"] = doc.Amount;
        var instanceId = await engine.SubmitAsync(payload, userId, userName);

        if (instanceId == -1)
        {
            // 未绑定流程=直通
            doc.Status = BizDocStatus.Approved;
            doc.InstanceId = null;
            await Repo.UpdateColumnsAsync(doc, "Status", "InstanceId");
        }
        else
        {
            doc.Status = BizDocStatus.Running;
            doc.InstanceId = instanceId;
            await Repo.UpdateColumnsAsync(doc, "Status", "InstanceId");
        }

        return ToDto(doc);
    }

    private void GuardEditable(BizExpense doc, long userId)
    {
        if (doc.OwnerUserId != userId) throw BizException.Forbidden("只能操作本人单据");
        if (doc.Status is BizDocStatus.Running or BizDocStatus.Approved)
            throw new BizException("审批中/已通过的单据不可编辑");
    }

    private async Task<string> NextDocNoAsync()
    {
        var day = DateTime.Now.ToString("yyyyMMdd");
        var prefix = "BX" + day;
        var count = await Repo.CountAsync(e => e.DocNo.StartsWith(prefix));
        return $"{prefix}{count + 1:D3}";
    }

    internal static string? SerializeIds(List<string> ids) =>
        ids.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(ids, Common.Json.JsonConfig.Options);

    internal static List<string> ParseIds(string? json)
    {
        if (string.IsNullOrEmpty(json)) return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json, Common.Json.JsonConfig.Options) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            return [];
        }
    }

    private static ExpenseDto ToDto(BizExpense e) => new()
    {
        Id = e.Id.ToString(), DocNo = e.DocNo, OwnerUserId = e.OwnerUserId?.ToString() ?? "",
        OwnerUserName = e.OwnerUserName, DeptId = e.DeptId?.ToString(), Amount = e.Amount,
        Category = e.Category, Reason = e.Reason, AttachmentIds = ParseIds(e.AttachmentIds),
        Status = e.Status, InstanceId = e.InstanceId?.ToString(), CreateTime = e.CreateTime, Version = e.Version
    };
}

/// <summary>报销单审批回调（按 BusinessTable 关联——红线：勿按流程编码）。</summary>
public class ExpenseFlowHandler(IRepository<BizExpense> repo) : IFlowBusinessHandler
{
    public string BusinessTable => ExpenseService.Table;

    public async Task<string?> GetSummaryAsync(long businessId)
    {
        var e = await repo.FindAsync(businessId);
        return e is null ? null : $"{e.OwnerUserName} 的报销单 {e.DocNo}（¥{e.Amount:N2}）";
    }

    public async Task OnFinishedAsync(SysFlowInstance instance)
    {
        var doc = await repo.FindAsync(instance.BusinessId);
        if (doc is null) return;
        doc.Status = instance.Status switch
        {
            FlowInstanceStatus.Approved => BizDocStatus.Approved,
            FlowInstanceStatus.Rejected => BizDocStatus.Rejected,
            _ => BizDocStatus.Withdrawn
        };
        await repo.UpdateColumnsAsync(doc, "Status");
    }
}

/// <summary>采购申请单（业务样板，模式同报销单）。</summary>
public class PurchaseService(
    IRepository<BizPurchaseRequest> repo,
    FlowEngineService engine,
    DataScopeService dataScope) : BaseService<BizPurchaseRequest>(repo)
{
    public const string Table = "biz_purchase_request";

    public async Task<PagedResult<PurchaseDto>> PageAsync(BizDocQuery query, long userId)
    {
        var kw = query.Keyword?.Trim();
        var ctx = query.Mine ? null : await dataScope.ResolveAsync(userId);
        var exp = Expressionable.Create<BizPurchaseRequest>();
        if (query.Mine) exp.And(p => p.OwnerUserId == userId);
        else exp.And(DataScopeService.Filter<BizPurchaseRequest>(ctx));
        if (!string.IsNullOrEmpty(kw)) exp.And(p => p.DocNo.Contains(kw) || p.ItemName.Contains(kw));
        if (query.Status is not null) exp.And(p => p.Status == query.Status!.Value);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<PurchaseDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    /// <summary>详情必须与列表同一套数据权限（同报销）。</summary>
    public async Task<PurchaseDto> GetAsync(long id, long userId)
    {
        var doc = await Repo.GetAsync(id);
        if (!DataScopeService.IsVisible(await dataScope.ResolveAsync(userId), doc))
            throw BizException.Forbidden("无权查看该单据");
        return ToDto(doc);
    }

    public async Task<PurchaseDto> CreateAsync(PurchaseSaveDto dto, long userId, string userName, long? deptId)
    {
        var day = DateTime.Now.ToString("yyyyMMdd");
        var count = await Repo.CountAsync(p => p.DocNo.StartsWith("CG" + day));
        var doc = new BizPurchaseRequest
        {
            DocNo = $"CG{day}{count + 1:D3}", OwnerUserId = userId, OwnerUserName = userName, DeptId = deptId,
            ItemName = dto.ItemName, Quantity = dto.Quantity, Amount = dto.Amount, Reason = dto.Reason,
            AttachmentIds = ExpenseService.SerializeIds(dto.AttachmentIds), Status = BizDocStatus.Draft
        };
        await Repo.InsertAsync(doc);
        return ToDto(doc);
    }

    public async Task UpdateAsync(long id, PurchaseSaveDto dto, long userId)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        doc.ItemName = dto.ItemName;
        doc.Quantity = dto.Quantity;
        doc.Amount = dto.Amount;
        doc.Reason = dto.Reason;
        doc.AttachmentIds = ExpenseService.SerializeIds(dto.AttachmentIds);
        doc.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(doc);
    }

    public async Task DeleteAsync(long id, long userId)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        await Repo.SoftDeleteAsync(id);
    }

    public async Task<PurchaseDto> SubmitAsync(long id, long userId, string userName, FlowSubmitDto payload)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        if (doc.Status == BizDocStatus.Running) throw new BizException("已在审批中");

        payload.BusinessTable = Table;
        payload.BusinessId = doc.Id;
        payload.Variables["amount"] = doc.Amount;
        var instanceId = await engine.SubmitAsync(payload, userId, userName);

        doc.Status = instanceId == -1 ? BizDocStatus.Approved : BizDocStatus.Running;
        doc.InstanceId = instanceId == -1 ? null : instanceId;
        await Repo.UpdateColumnsAsync(doc, "Status", "InstanceId");
        return ToDto(doc);
    }

    private void GuardEditable(BizPurchaseRequest doc, long userId)
    {
        if (doc.OwnerUserId != userId) throw BizException.Forbidden("只能操作本人单据");
        if (doc.Status is BizDocStatus.Running or BizDocStatus.Approved)
            throw new BizException("审批中/已通过的单据不可编辑");
    }

    private static PurchaseDto ToDto(BizPurchaseRequest p) => new()
    {
        Id = p.Id.ToString(), DocNo = p.DocNo, OwnerUserId = p.OwnerUserId?.ToString() ?? "",
        OwnerUserName = p.OwnerUserName, DeptId = p.DeptId?.ToString(), ItemName = p.ItemName,
        Quantity = p.Quantity, Amount = p.Amount, Reason = p.Reason,
        AttachmentIds = ExpenseService.ParseIds(p.AttachmentIds), Status = p.Status,
        InstanceId = p.InstanceId?.ToString(), CreateTime = p.CreateTime, Version = p.Version
    };
}

public class PurchaseFlowHandler(IRepository<BizPurchaseRequest> repo) : IFlowBusinessHandler
{
    public string BusinessTable => PurchaseService.Table;

    public async Task<string?> GetSummaryAsync(long businessId)
    {
        var p = await repo.FindAsync(businessId);
        return p is null ? null : $"{p.OwnerUserName} 的采购申请 {p.DocNo}（{p.ItemName} ×{p.Quantity}）";
    }

    public async Task OnFinishedAsync(SysFlowInstance instance)
    {
        var doc = await repo.FindAsync(instance.BusinessId);
        if (doc is null) return;
        doc.Status = instance.Status switch
        {
            FlowInstanceStatus.Approved => BizDocStatus.Approved,
            FlowInstanceStatus.Rejected => BizDocStatus.Rejected,
            _ => BizDocStatus.Withdrawn
        };
        await repo.UpdateColumnsAsync(doc, "Status");
    }
}
