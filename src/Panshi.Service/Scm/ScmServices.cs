using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using Panshi.Service.Flow;
using Panshi.Service.Md;
using Panshi.Service.Sys;
using SqlSugar;

namespace Panshi.Service.Scm;

/// <summary>解析后的明细行（物料快照已取好、金额已算好），采购与销售共用同一份算法。</summary>
internal readonly record struct ResolvedLine(
    long MaterialId, string Code, string Name, string? Spec, string? Unit,
    decimal Qty, decimal Price, decimal TaxRate, decimal Amount, string? Remark);

/// <summary>
/// 订单明细的共同规则：物料快照解析 + 金额口径。
/// 抽出来的理由是「同一套算法不能有两份四舍五入口径」——采购和销售若各写一遍，
/// 迟早会出现同一张单在两个模块里合计差一分。
/// </summary>
internal static class ScmOrderRules
{
    /// <summary>行金额 = 数量 × 含税单价，四舍五入到分（AwayFromZero，与前端展示口径一致）。</summary>
    internal static decimal AmountOf(decimal qty, decimal price)
        => decimal.Round(qty * price, 2, MidpointRounding.AwayFromZero);

    internal static async Task<List<ResolvedLine>> ResolveAsync(IRepository<MdMaterial> materials,
        List<OrderLineSaveDto> lines)
    {
        if (lines.Count == 0) throw new BizException("请至少录入一行明细");

        var ids = lines.Select(l => ParseMaterialId(l.MaterialId)).Distinct().ToList();
        var known = (await materials.ListAsync(m => ids.Contains(m.Id))).ToDictionary(m => m.Id);

        var resolved = new List<ResolvedLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var materialId = ParseMaterialId(line.MaterialId);
            if (!known.TryGetValue(materialId, out var material))
                throw new BizException($"第 {i + 1} 行的物料不存在或已被删除");
            if (line.Quantity <= 0) throw new BizException($"第 {i + 1} 行数量必须大于 0");
            if (line.UnitPrice < 0) throw new BizException($"第 {i + 1} 行单价不能为负");
            if (line.TaxRate is < 0 or > 100) throw new BizException($"第 {i + 1} 行税率需在 0~100 之间");

            resolved.Add(new ResolvedLine(materialId, material.MaterialCode, material.MaterialName, material.Spec,
                material.Unit, line.Quantity, line.UnitPrice, line.TaxRate, AmountOf(line.Quantity, line.UnitPrice),
                Blank.ToNull(line.Remark)));
        }

        return resolved;
    }

    internal static (decimal TotalQty, decimal TotalAmount) Sum(IEnumerable<ResolvedLine> lines)
        => (lines.Sum(l => l.Qty), decimal.Round(lines.Sum(l => l.Amount), 2, MidpointRounding.AwayFromZero));

    private static long ParseMaterialId(string raw)
        => long.TryParse(raw, out var id) && id > 0 ? id : throw new BizException("请选择物料");
}

/// <summary>
/// 采购订单。头 + 明细两张表，写入走事务（红线 #3）；
/// 审批按 business_table=scm_purchase_order 绑定，与报销/采购申请同一套引擎语义。
/// </summary>
public class PurchaseOrderService(
    IRepository<ScmPurchaseOrder> repo,
    IRepository<ScmPurchaseOrderLine> lineRepo,
    IRepository<MdSupplier> suppliers,
    IRepository<MdMaterial> materials,
    IRepository<BizPurchaseRequest> requests,
    FlowEngineService engine,
    DataScopeService dataScope) : BaseService<ScmPurchaseOrder>(repo)
{
    public const string Table = "scm_purchase_order";

    public async Task<PagedResult<PurchaseOrderDto>> PageAsync(ScmDocQuery query, long userId)
    {
        var kw = query.Keyword?.Trim();
        var ctx = query.Mine ? null : await dataScope.ResolveAsync(userId);
        var exp = Expressionable.Create<ScmPurchaseOrder>();
        if (query.Mine) exp.And(o => o.OwnerUserId == userId);
        else exp.And(DataScopeService.Filter<ScmPurchaseOrder>(ctx));
        if (!string.IsNullOrEmpty(kw)) exp.And(o => o.DocNo.Contains(kw) || o.SupplierName.Contains(kw));
        if (query.Status is BizDocStatus status) exp.And(o => o.Status == status);
        if (long.TryParse(query.SupplierId, out var supplierId)) exp.And(o => o.SupplierId == supplierId);
        // 端点含头含尾：前端传的 end 已是当天 23:59:59（本地时区）
        if (query.Begin is DateTime begin) exp.And(o => o.OrderDate >= begin);
        if (query.End is DateTime end) exp.And(o => o.OrderDate <= end);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        var rows = page.Rows.Select(ToDto).ToList();
        await FillLineCountsAsync(rows);
        return new PagedResult<PurchaseOrderDto> { Total = page.Total, Rows = rows };
    }

    /// <summary>详情必须与列表同一套数据权限，否则「仅本人」的人按 id 就能读到别人的订单。</summary>
    public async Task<PurchaseOrderDto> GetAsync(long id, long userId)
    {
        var doc = await Repo.GetAsync(id);
        if (!DataScopeService.IsVisible(await dataScope.ResolveAsync(userId), doc))
            throw BizException.Forbidden("无权查看该单据");
        var dto = ToDto(doc);
        dto.Lines = (await lineRepo.ListAsync(l => l.OrderId == id)).Select(ToLineDto).ToList();
        return dto;
    }

    public async Task<PurchaseOrderDto> CreateAsync(PurchaseOrderSaveDto dto, long userId, string userName, long? deptId)
    {
        if (!long.TryParse(dto.SupplierId, out var supplierId) || supplierId <= 0) throw new BizException("请选择供应商");
        var supplier = await suppliers.FindAsync(supplierId) ?? throw new BizException("供应商不存在");
        var lines = await ScmOrderRules.ResolveAsync(materials, dto.Lines);
        var source = await ResolveSourceAsync(dto.SourceRequestId);
        var (totalQty, totalAmount) = ScmOrderRules.Sum(lines);

        var doc = new ScmPurchaseOrder
        {
            DocNo = await NextDocNoAsync(), OwnerUserId = userId, OwnerUserName = userName, DeptId = deptId,
            SupplierId = supplierId, SupplierName = supplier.SupplierName, OrderDate = dto.OrderDate.Date,
            DeliveryDate = dto.DeliveryDate?.Date, TotalQty = totalQty, TotalAmount = totalAmount,
            SourceRequestId = source?.Id, SourceRequestNo = source?.DocNo, Remark = Blank.ToNull(dto.Remark),
            Status = BizDocStatus.Draft
        };

        return await Tran.RunAsync(Repo.Db, async () =>
        {
            await Repo.InsertAsync(doc);
            await InsertLinesAsync(doc.Id, lines);
            return ToDto(doc);
        });
    }

    /// <summary>编辑=整单覆盖明细（明细无独立历史价值，逐行 diff 只会把变更日志刷成噪音）。</summary>
    public async Task UpdateAsync(long id, PurchaseOrderSaveDto dto, long userId)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        if (!long.TryParse(dto.SupplierId, out var supplierId) || supplierId <= 0) throw new BizException("请选择供应商");
        var supplier = await suppliers.FindAsync(supplierId) ?? throw new BizException("供应商不存在");
        var lines = await ScmOrderRules.ResolveAsync(materials, dto.Lines);
        var source = await ResolveSourceAsync(dto.SourceRequestId);
        var (totalQty, totalAmount) = ScmOrderRules.Sum(lines);

        doc.SupplierId = supplierId;
        doc.SupplierName = supplier.SupplierName;
        doc.OrderDate = dto.OrderDate.Date;
        doc.DeliveryDate = dto.DeliveryDate?.Date;
        doc.TotalQty = totalQty;
        doc.TotalAmount = totalAmount;
        doc.SourceRequestId = source?.Id;
        doc.SourceRequestNo = source?.DocNo;
        doc.Remark = Blank.ToNull(dto.Remark);
        doc.Version = dto.Version;

        await Tran.RunAsync(Repo.Db, async () =>
        {
            await UpdateWithConcurrencyCheckAsync(doc);
            await lineRepo.DeleteAsync(l => l.OrderId == id);
            await InsertLinesAsync(id, lines);
        });
    }

    public async Task DeleteAsync(long id, long userId)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        await Tran.RunAsync(Repo.Db, async () =>
        {
            await Repo.SoftDeleteAsync(id);
            await lineRepo.SoftDeleteAsync(l => l.OrderId == id);
        });
    }

    /// <summary>提交审批（草稿/拒绝/撤回后可重提=新实例从头走；未绑定流程=直通通过）。</summary>
    public async Task<PurchaseOrderDto> SubmitAsync(long id, long userId, string userName, FlowSubmitDto payload)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        if (doc.Status == BizDocStatus.Running) throw new BizException("已在审批中");
        if (await lineRepo.CountAsync(l => l.OrderId == id) == 0) throw new BizException("明细为空，不能提交");

        payload.BusinessTable = Table;
        payload.BusinessId = doc.Id;
        payload.Variables["amount"] = doc.TotalAmount;
        payload.Variables["supplierId"] = doc.SupplierId;
        var instanceId = await engine.SubmitAsync(payload, userId, userName);

        if (instanceId == -1)
        {
            doc.Status = BizDocStatus.Approved;
            doc.InstanceId = null;
            await Repo.UpdateColumnsAsync(doc, "Status", "InstanceId");
        }
        else
        {
            doc.InstanceId = instanceId;
            // 实例可能这一次提交就走完了：终态归 OnFinishedAsync 写，这里再写 Running 就是盖成死单
            if (await engine.IsOpenAsync(instanceId))
            {
                doc.Status = BizDocStatus.Running;
                await Repo.UpdateColumnsAsync(doc, "Status", "InstanceId");
            }
            else
            {
                await Repo.UpdateColumnsAsync(doc, "InstanceId");
                // 终态是回调在引擎事务里写的，不重读就会给前端回一个「草稿」
                doc = await Repo.GetAsync(id);
            }
        }
        return ToDto(doc);
    }

    /// <summary>整页一次分组查行数：列表要显示「几 行」，逐行查会变成 N+1。</summary>
    private async Task FillLineCountsAsync(List<PurchaseOrderDto> rows)
    {
        if (rows.Count == 0) return;
        var ids = rows.Select(r => long.Parse(r.Id)).ToList();
        var counts = (await lineRepo.ListAsync(l => ids.Contains(l.OrderId)))
            .GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var r in rows) r.LineCount = counts.GetValueOrDefault(long.Parse(r.Id));
    }

    private void GuardEditable(ScmPurchaseOrder doc, long userId)
    {
        if (doc.OwnerUserId != userId) throw BizException.Forbidden("只能操作本人单据");
        if (doc.Status is BizDocStatus.Running or BizDocStatus.Approved)
            throw new BizException("审批中/已通过的订单不可编辑");
    }

    private async Task<BizPurchaseRequest?> ResolveSourceAsync(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!long.TryParse(raw, out var id) || id <= 0) throw new BizException("来源采购申请单不合法");
        return await requests.FindAsync(id) ?? throw new BizException("来源采购申请单不存在");
    }

    private async Task InsertLinesAsync(long orderId, List<ResolvedLine> lines)
        => await lineRepo.InsertRangeAsync(lines.Select(l => new ScmPurchaseOrderLine
        {
            OrderId = orderId, MaterialId = l.MaterialId, MaterialCode = l.Code, MaterialName = l.Name,
            Spec = l.Spec, Unit = l.Unit, Quantity = l.Qty, UnitPrice = l.Price, TaxRate = l.TaxRate,
            Amount = l.Amount, Remark = l.Remark
        }).ToList());

    private async Task<string> NextDocNoAsync()
    {
        var prefix = "PO" + DateTime.Now.ToString("yyyyMMdd");
        var count = await Repo.CountAsync(o => o.DocNo.StartsWith(prefix));
        return $"{prefix}{count + 1:D3}";
    }

    private static OrderLineDto ToLineDto(ScmPurchaseOrderLine l) => new()
    {
        Id = l.Id.ToString(), MaterialId = l.MaterialId.ToString(), MaterialCode = l.MaterialCode,
        MaterialName = l.MaterialName, Spec = l.Spec, Unit = l.Unit, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
        TaxRate = l.TaxRate, Amount = l.Amount, Remark = l.Remark
    };

    private static PurchaseOrderDto ToDto(ScmPurchaseOrder o) => new()
    {
        Id = o.Id.ToString(), DocNo = o.DocNo, OwnerUserId = o.OwnerUserId?.ToString() ?? "",
        OwnerUserName = o.OwnerUserName, SupplierId = o.SupplierId.ToString(), SupplierName = o.SupplierName,
        OrderDate = o.OrderDate, DeliveryDate = o.DeliveryDate, TotalQty = o.TotalQty, TotalAmount = o.TotalAmount,
        SourceRequestId = o.SourceRequestId?.ToString(), SourceRequestNo = o.SourceRequestNo, Status = o.Status,
        InstanceId = o.InstanceId?.ToString(), Remark = o.Remark, CreateTime = o.CreateTime, Version = o.Version
    };
}

/// <summary>采购订单审批回调（按 BusinessTable 关联——红线：勿按流程编码）。</summary>
public class PurchaseOrderFlowHandler(
    IRepository<ScmPurchaseOrder> repo,
    IRepository<ScmPurchaseOrderLine> lineRepo) : IFlowBusinessHandler
{
    public string BusinessTable => PurchaseOrderService.Table;

    public async Task<string?> GetSummaryAsync(long businessId)
    {
        var doc = await repo.FindAsync(businessId);
        if (doc is null) return null;
        var lines = await lineRepo.CountAsync(l => l.OrderId == doc.Id);
        return $"{doc.OwnerUserName} 的采购订单 {doc.DocNo}（{doc.SupplierName}，{lines} 行 ¥{doc.TotalAmount:N2}）";
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

/// <summary>销售订单（模式同采购订单，分表是为了各自绑不同的审批流）。</summary>
public class SalesOrderService(
    IRepository<ScmSalesOrder> repo,
    IRepository<ScmSalesOrderLine> lineRepo,
    IRepository<MdCustomer> customers,
    IRepository<MdMaterial> materials,
    IRepository<MdWarehouse> warehouses,
    FlowEngineService engine,
    DataScopeService dataScope) : BaseService<ScmSalesOrder>(repo)
{
    public const string Table = "scm_sales_order";

    public async Task<PagedResult<SalesOrderDto>> PageAsync(ScmDocQuery query, long userId)
    {
        var kw = query.Keyword?.Trim();
        var ctx = query.Mine ? null : await dataScope.ResolveAsync(userId);
        var exp = Expressionable.Create<ScmSalesOrder>();
        if (query.Mine) exp.And(o => o.OwnerUserId == userId);
        else exp.And(DataScopeService.Filter<ScmSalesOrder>(ctx));
        if (!string.IsNullOrEmpty(kw)) exp.And(o => o.DocNo.Contains(kw) || o.CustomerName.Contains(kw));
        if (query.Status is BizDocStatus status) exp.And(o => o.Status == status);
        if (long.TryParse(query.CustomerId, out var customerId)) exp.And(o => o.CustomerId == customerId);
        if (query.Begin is DateTime begin) exp.And(o => o.OrderDate >= begin);
        if (query.End is DateTime end) exp.And(o => o.OrderDate <= end);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        var rows = page.Rows.Select(ToDto).ToList();
        await FillLineCountsAsync(rows);
        return new PagedResult<SalesOrderDto> { Total = page.Total, Rows = rows };
    }

    public async Task<SalesOrderDto> GetAsync(long id, long userId)
    {
        var doc = await Repo.GetAsync(id);
        if (!DataScopeService.IsVisible(await dataScope.ResolveAsync(userId), doc))
            throw BizException.Forbidden("无权查看该单据");
        var dto = ToDto(doc);
        dto.Lines = (await lineRepo.ListAsync(l => l.OrderId == id)).Select(ToLineDto).ToList();
        return dto;
    }

    public async Task<SalesOrderDto> CreateAsync(SalesOrderSaveDto dto, long userId, string userName, long? deptId)
    {
        if (!long.TryParse(dto.CustomerId, out var customerId) || customerId <= 0) throw new BizException("请选择客户");
        var customer = await customers.FindAsync(customerId) ?? throw new BizException("客户不存在");
        var (warehouseId, warehouseName) = await ResolveWarehouseAsync(dto.WarehouseId);
        var lines = await ScmOrderRules.ResolveAsync(materials, dto.Lines);
        var (totalQty, totalAmount) = ScmOrderRules.Sum(lines);

        var doc = new ScmSalesOrder
        {
            DocNo = await NextDocNoAsync(), OwnerUserId = userId, OwnerUserName = userName, DeptId = deptId,
            CustomerId = customerId, CustomerName = customer.CustomerName, OrderDate = dto.OrderDate.Date,
            DeliveryDate = dto.DeliveryDate?.Date, TotalQty = totalQty, TotalAmount = totalAmount,
            WarehouseId = warehouseId, WarehouseName = warehouseName, Remark = Blank.ToNull(dto.Remark),
            Status = BizDocStatus.Draft
        };

        return await Tran.RunAsync(Repo.Db, async () =>
        {
            await Repo.InsertAsync(doc);
            await InsertLinesAsync(doc.Id, lines);
            return ToDto(doc);
        });
    }

    public async Task UpdateAsync(long id, SalesOrderSaveDto dto, long userId)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        if (!long.TryParse(dto.CustomerId, out var customerId) || customerId <= 0) throw new BizException("请选择客户");
        var customer = await customers.FindAsync(customerId) ?? throw new BizException("客户不存在");
        var (warehouseId, warehouseName) = await ResolveWarehouseAsync(dto.WarehouseId);
        var lines = await ScmOrderRules.ResolveAsync(materials, dto.Lines);
        var (totalQty, totalAmount) = ScmOrderRules.Sum(lines);

        doc.CustomerId = customerId;
        doc.CustomerName = customer.CustomerName;
        doc.WarehouseId = warehouseId;
        doc.WarehouseName = warehouseName;
        doc.OrderDate = dto.OrderDate.Date;
        doc.DeliveryDate = dto.DeliveryDate?.Date;
        doc.TotalQty = totalQty;
        doc.TotalAmount = totalAmount;
        doc.Remark = Blank.ToNull(dto.Remark);
        doc.Version = dto.Version;

        await Tran.RunAsync(Repo.Db, async () =>
        {
            await UpdateWithConcurrencyCheckAsync(doc);
            await lineRepo.DeleteAsync(l => l.OrderId == id);
            await InsertLinesAsync(id, lines);
        });
    }

    public async Task DeleteAsync(long id, long userId)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        await Tran.RunAsync(Repo.Db, async () =>
        {
            await Repo.SoftDeleteAsync(id);
            await lineRepo.SoftDeleteAsync(l => l.OrderId == id);
        });
    }

    public async Task<SalesOrderDto> SubmitAsync(long id, long userId, string userName, FlowSubmitDto payload)
    {
        var doc = await Repo.GetAsync(id);
        GuardEditable(doc, userId);
        if (doc.Status == BizDocStatus.Running) throw new BizException("已在审批中");
        if (await lineRepo.CountAsync(l => l.OrderId == id) == 0) throw new BizException("明细为空，不能提交");

        payload.BusinessTable = Table;
        payload.BusinessId = doc.Id;
        payload.Variables["amount"] = doc.TotalAmount;
        payload.Variables["customerId"] = doc.CustomerId;
        var instanceId = await engine.SubmitAsync(payload, userId, userName);

        if (instanceId == -1)
        {
            doc.Status = BizDocStatus.Approved;
            doc.InstanceId = null;
            await Repo.UpdateColumnsAsync(doc, "Status", "InstanceId");
        }
        else
        {
            doc.InstanceId = instanceId;
            // 实例可能这一次提交就走完了：终态归 OnFinishedAsync 写，这里再写 Running 就是盖成死单
            if (await engine.IsOpenAsync(instanceId))
            {
                doc.Status = BizDocStatus.Running;
                await Repo.UpdateColumnsAsync(doc, "Status", "InstanceId");
            }
            else
            {
                await Repo.UpdateColumnsAsync(doc, "InstanceId");
                // 终态是回调在引擎事务里写的，不重读就会给前端回一个「草稿」
                doc = await Repo.GetAsync(id);
            }
        }
        return ToDto(doc);
    }

    private async Task FillLineCountsAsync(List<SalesOrderDto> rows)
    {
        if (rows.Count == 0) return;
        var ids = rows.Select(r => long.Parse(r.Id)).ToList();
        var counts = (await lineRepo.ListAsync(l => ids.Contains(l.OrderId)))
            .GroupBy(l => l.OrderId).ToDictionary(g => g.Key, g => g.Count());
        foreach (var r in rows) r.LineCount = counts.GetValueOrDefault(long.Parse(r.Id));
    }

    private void GuardEditable(ScmSalesOrder doc, long userId)
    {
        if (doc.OwnerUserId != userId) throw BizException.Forbidden("只能操作本人单据");
        if (doc.Status is BizDocStatus.Running or BizDocStatus.Approved)
            throw new BizException("审批中/已通过的订单不可编辑");
    }

    private async Task<(long? Id, string? Name)> ResolveWarehouseAsync(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, null);
        if (!long.TryParse(raw, out var id) || id <= 0) throw new BizException("发货仓库不合法");
        var warehouse = await warehouses.FindAsync(id) ?? throw new BizException("仓库不存在");
        return (id, warehouse.WarehouseName);
    }

    private async Task InsertLinesAsync(long orderId, List<ResolvedLine> lines)
        => await lineRepo.InsertRangeAsync(lines.Select(l => new ScmSalesOrderLine
        {
            OrderId = orderId, MaterialId = l.MaterialId, MaterialCode = l.Code, MaterialName = l.Name,
            Spec = l.Spec, Unit = l.Unit, Quantity = l.Qty, UnitPrice = l.Price, TaxRate = l.TaxRate,
            Amount = l.Amount, Remark = l.Remark
        }).ToList());

    private async Task<string> NextDocNoAsync()
    {
        var prefix = "SO" + DateTime.Now.ToString("yyyyMMdd");
        var count = await Repo.CountAsync(o => o.DocNo.StartsWith(prefix));
        return $"{prefix}{count + 1:D3}";
    }

    private static OrderLineDto ToLineDto(ScmSalesOrderLine l) => new()
    {
        Id = l.Id.ToString(), MaterialId = l.MaterialId.ToString(), MaterialCode = l.MaterialCode,
        MaterialName = l.MaterialName, Spec = l.Spec, Unit = l.Unit, Quantity = l.Quantity, UnitPrice = l.UnitPrice,
        TaxRate = l.TaxRate, Amount = l.Amount, Remark = l.Remark
    };

    private static SalesOrderDto ToDto(ScmSalesOrder o) => new()
    {
        Id = o.Id.ToString(), DocNo = o.DocNo, OwnerUserId = o.OwnerUserId?.ToString() ?? "",
        OwnerUserName = o.OwnerUserName, CustomerId = o.CustomerId.ToString(), CustomerName = o.CustomerName,
        OrderDate = o.OrderDate, DeliveryDate = o.DeliveryDate, TotalQty = o.TotalQty, TotalAmount = o.TotalAmount,
        WarehouseId = o.WarehouseId?.ToString(), WarehouseName = o.WarehouseName, Status = o.Status,
        InstanceId = o.InstanceId?.ToString(), Remark = o.Remark, CreateTime = o.CreateTime, Version = o.Version
    };
}

/// <summary>销售订单审批回调。</summary>
public class SalesOrderFlowHandler(
    IRepository<ScmSalesOrder> repo,
    IRepository<ScmSalesOrderLine> lineRepo) : IFlowBusinessHandler
{
    public string BusinessTable => SalesOrderService.Table;

    public async Task<string?> GetSummaryAsync(long businessId)
    {
        var doc = await repo.FindAsync(businessId);
        if (doc is null) return null;
        var lines = await lineRepo.CountAsync(l => l.OrderId == doc.Id);
        return $"{doc.OwnerUserName} 的销售订单 {doc.DocNo}（{doc.CustomerName}，{lines} 行 ¥{doc.TotalAmount:N2}）";
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
