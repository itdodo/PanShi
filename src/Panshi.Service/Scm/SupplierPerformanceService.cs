using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;

namespace Panshi.Service.Scm;

/// <summary>
/// 供应商绩效（只读）。口径说明：
/// 订单↔入库靠「采购入库单的回链来源单号 = 采购订单号」关联，所以没回填单号的入库会被算成「未到货」——
/// 报表如实标出这一列而不是把它当零，避免读的人以为供应商真的没交货。
/// 准交率的分母只算「已到货且填了计划交期」的订单：没有计划交期就不该编出一个比率，返回 null 而不是 0。
/// </summary>
public class SupplierPerformanceService(
    IRepository<ScmPurchaseOrder> orders,
    IRepository<ScmStockDoc> docs)
{
    public async Task<PagedResult<SupplierPerformanceDto>> PageAsync(SupplierPerformanceQuery query)
    {
        var begin = (query.Begin ?? DateTime.Today.AddYears(-1)).Date;
        var endExclusive = (query.End ?? DateTime.Now).Date.AddDays(1);
        if (endExclusive <= begin) throw new BizException("结束日期不能早于起始日期");

        var placed = await orders.ListAsync(o =>
            o.Status == BizDocStatus.Approved && o.OrderDate >= begin && o.OrderDate < endExclusive);

        // 首次入库时间：过账时刻（草稿不算，作废的整单已被反向冲销所以按 Posted 过滤）
        var inbound = await docs.ListAsync(d =>
            d.Kind == StockDocKind.PurchaseIn && d.Status == StockDocStatus.Posted && d.SourceOrderNo != null);
        var firstIn = inbound
            .Where(d => !string.IsNullOrWhiteSpace(d.SourceOrderNo))
            .GroupBy(d => d.SourceOrderNo!.Trim())
            .ToDictionary(g => g.Key, g => g.Min(d => d.PostedTime ?? d.CreateTime));

        var keyword = query.Keyword?.Trim();
        var rows = new List<SupplierPerformanceDto>();
        foreach (var group in placed.GroupBy(o => o.SupplierId))
        {
            var list = group.ToList();
            var name = list[0].SupplierName;
            if (!string.IsNullOrEmpty(keyword) && !name.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;

            var delivered = list.Where(o => firstIn.ContainsKey(o.DocNo)).ToList();
            var withPlan = delivered.Where(o => o.DeliveryDate is not null).ToList();
            var leadDays = delivered.Select(o => (decimal)(firstIn[o.DocNo].Date - o.OrderDate.Date).TotalDays).ToList();

            var dto = new SupplierPerformanceDto
            {
                SupplierId = group.Key.ToString(),
                SupplierName = string.IsNullOrEmpty(name) ? $"#{group.Key}" : name,
                Orders = list.Count,
                Amount = list.Sum(o => o.TotalAmount),
                Delivered = delivered.Count,
                OnTimeBase = withPlan.Count,
                OnTime = withPlan.Count(o => firstIn[o.DocNo].Date <= o.DeliveryDate!.Value.Date),
                Pending = list.Count - delivered.Count,
                AvgLeadDays = leadDays.Count == 0 ? 0m : Math.Round(leadDays.Average(), 1)
            };
            dto.OnTimeRate = dto.OnTimeBase == 0 ? null : Math.Round((decimal)dto.OnTime / dto.OnTimeBase, 4);
            rows.Add(dto);
        }

        var sorted = rows.OrderByDescending(r => r.Amount).ThenBy(r => r.SupplierName, StringComparer.Ordinal).ToList();
        return new PagedResult<SupplierPerformanceDto>
        {
            Total = sorted.Count,
            Rows = sorted.Skip((query.PageNum - 1) * query.PageSize).Take(query.PageSize).ToList()
        };
    }
}
