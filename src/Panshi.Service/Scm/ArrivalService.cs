using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using Panshi.Service.Md;
using Panshi.Service.Sys;
using SqlSugar;

namespace Panshi.Service.Scm;

/// <summary>
/// 采购到货计划。一条已批准的订单行对应一条计划，表里**只存计划**：
/// 实际到货量在查询时从「已过账 + 回链了本订单号」的采购入库单实时汇总，
/// 所以过账路径一行都不用改，作废/红冲也自动反映到未收量上。
/// 状态与逾期同样全部推导不落库——存了就要在改期、入库、作废三处同步，早晚对不上。
/// </summary>
public class ArrivalService(
    IRepository<ScmPurchaseArrival> repo,
    IRepository<ScmPurchaseOrder> orders,
    IRepository<ScmPurchaseOrderLine> lines,
    IRepository<ScmStockDoc> docs,
    IRepository<ScmStockDocLine> docLines,
    DataScopeService dataScope) : BaseService<ScmPurchaseArrival>(repo)
{
    /// <summary>
    /// 订单批准后按行补计划。幂等且只补不覆盖：人工改过的计划（含改期）不会被重新生成冲掉。
    /// </summary>
    public async Task GenerateForOrderAsync(long orderId)
    {
        var order = await orders.FindAsync(orderId);
        if (order is null || order.Status != BizDocStatus.Approved) return;
        var orderLines = await lines.ListAsync(l => l.OrderId == orderId);
        if (orderLines.Count == 0) return;

        var known = (await repo.ListAsync(a => a.OrderId == orderId)).Select(a => a.OrderLineId).ToHashSet();
        var planDate = (order.DeliveryDate ?? DateTime.Today).Date;
        var fresh = orderLines.Where(l => !known.Contains(l.Id))
            .Select(l => new ScmPurchaseArrival
            {
                OrderId = order.Id, OrderNo = order.DocNo, OrderLineId = l.Id, SupplierId = order.SupplierId,
                SupplierName = order.SupplierName, MaterialId = l.MaterialId, MaterialCode = l.MaterialCode,
                MaterialName = l.MaterialName, Spec = l.Spec, Unit = l.Unit, PlanQty = l.Quantity,
                PlanDate = planDate, OwnerUserId = order.OwnerUserId, OwnerUserName = order.OwnerUserName,
                DeptId = order.DeptId, Remark = l.Remark
            }).ToList();
        if (fresh.Count > 0) await repo.InsertRangeAsync(fresh);
    }

    public async Task<PagedResult<ArrivalDto>> PageAsync(ArrivalQuery query, long userId)
    {
        var kw = query.Keyword?.Trim();
        var exp = Expressionable.Create<ScmPurchaseArrival>();
        if (query.Mine) exp.And(a => a.OwnerUserId == userId);
        else exp.And(DataScopeService.Filter<ScmPurchaseArrival>(await dataScope.ResolveAsync(userId)));
        if (!string.IsNullOrEmpty(kw)) exp.And(a => a.OrderNo.Contains(kw) || a.SupplierName.Contains(kw)
            || a.MaterialCode.Contains(kw) || a.MaterialName.Contains(kw));
        if (long.TryParse(query.SupplierId, out var supplierId)) exp.And(a => a.SupplierId == supplierId);

        // 状态/逾期是推导出来的，只能先取回再筛。要按历史查时传 DueBefore 收窄区间
        var all = await repo.ListAsync(exp.ToExpression());
        var received = await ReceivedMapAsync(all.Select(a => a.OrderNo).Distinct().ToList());
        var today = DateTime.Today;

        var rows = new List<ArrivalDto>();
        foreach (var a in all)
        {
            var got = received.TryGetValue((a.OrderNo, a.MaterialId), out var v) ? v : 0m;
            var open = a.PlanQty - got;
            var status = got <= 0m ? ArrivalStatus.Pending : open > 0m ? ArrivalStatus.Partial : ArrivalStatus.Done;
            var overdue = open > 0m && a.PlanDate.Date < today;
            if (query.Status is { } want && want != status) continue;
            if (query.OverdueOnly && !overdue) continue;
            if (query.DueBefore is DateTime due && a.PlanDate.Date > due.Date) continue;

            rows.Add(new ArrivalDto
            {
                Id = a.Id.ToString(), OrderId = a.OrderId.ToString(), OrderNo = a.OrderNo,
                OrderLineId = a.OrderLineId.ToString(), SupplierId = a.SupplierId.ToString(),
                SupplierName = a.SupplierName, MaterialId = a.MaterialId.ToString(), MaterialCode = a.MaterialCode,
                MaterialName = a.MaterialName, Spec = a.Spec, Unit = a.Unit, PlanQty = a.PlanQty,
                PlanDate = a.PlanDate, ReceivedQty = got, OpenQty = open, Status = status, Overdue = overdue,
                Rescheduled = a.Rescheduled, OwnerUserName = a.OwnerUserName, Remark = a.Remark,
                CreateTime = a.CreateTime, Version = a.Version
            });
        }

        // 逾期的、计划日靠前的先看
        var sorted = rows.OrderBy(r => r.Overdue ? 0 : 1).ThenBy(r => r.PlanDate).ThenBy(r => r.SupplierName, StringComparer.Ordinal)
            .ToList();
        return new PagedResult<ArrivalDto>
        {
            Total = sorted.Count,
            Rows = sorted.Skip((query.PageNum - 1) * query.PageSize).Take(query.PageSize).ToList()
        };
    }

    public async Task RescheduleAsync(long id, ArrivalRescheduleDto dto)
    {
        var a = await Repo.GetAsync(id);
        a.PlanDate = dto.PlanDate.Date;
        a.Rescheduled = true;
        var remark = Blank.ToNull(dto.Remark, r => r);
        if (remark is not null) a.Remark = remark;
        a.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(a);
    }

    /// <summary>「订单号 + 物料」→ 已过账采购入库量。草稿与已作废的单据都不计入。</summary>
    private async Task<Dictionary<(string OrderNo, long MaterialId), decimal>> ReceivedMapAsync(List<string> orderNos)
    {
        var map = new Dictionary<(string, long), decimal>();
        if (orderNos.Count == 0) return map;

        var ins = await docs.ListAsync(d => d.Kind == StockDocKind.PurchaseIn
            && d.Status == StockDocStatus.Posted && d.SourceOrderNo != null && orderNos.Contains(d.SourceOrderNo));
        if (ins.Count == 0) return map;

        var docToOrder = ins.ToDictionary(d => d.Id, d => d.SourceOrderNo!.Trim());
        var ids = docToOrder.Keys.ToList();
        var rows = await docLines.ListAsync(l => ids.Contains(l.DocId));
        foreach (var g in rows.GroupBy(l => (docToOrder[l.DocId], l.MaterialId)))
            map[g.Key] = g.Sum(l => l.Quantity);
        return map;
    }
}
