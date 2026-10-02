using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Repository;
using SqlSugar;

namespace Panshi.Service.Scm;

/// <summary>
/// 进销存汇总：按「仓库 × 物料」给出 期初 / 收入 / 发出 / 期末。
/// 全部聚合下推到 SQL——流水是只增账本，拉到内存里累加会随着时间无界膨胀。
/// 期初取区间起点之前的累计和，所以「期初 + 收入 − 发出 = 期末」这条恒等式对任意区间都成立，
/// 不依赖 scm_stock 的当前值（那等于只有区间到今天时才成立）。
/// </summary>
public class StockSummaryService(
    IRepository<ScmStockLedger> ledger,
    IRepository<MdMaterial> materials,
    IRepository<MdWarehouse> warehouses)
{
    /// <summary>一条分组聚合的落点（列名由 SqlSugar 按属性映射）</summary>
    private sealed class Agg
    {
        public long WarehouseId { get; set; }

        public long MaterialId { get; set; }

        public decimal Qty { get; set; }

        public int Cnt { get; set; }
    }

    public async Task<PagedResult<StockSummaryDto>> PageAsync(StockSummaryQuery query)
    {
        if (query.Begin is not DateTime rawBegin) throw new BizException("请选择统计起始日期");
        var begin = rawBegin.Date;
        // 含头含尾：截止日按整天算到次日零点为止（不含），与前端传 00:00:00 还是 23:59:59 无关
        var endExclusive = ((query.End ?? DateTime.Now).Date).AddDays(1);
        if (endExclusive <= begin) throw new BizException("结束日期不能早于起始日期");

        long? warehouseId = long.TryParse(query.WarehouseId, out var wid) && wid > 0 ? wid : null;
        List<long>? materialIds = null;
        var keyword = query.Keyword?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            materialIds = (await materials.ListAsync(m => m.MaterialName.Contains(keyword) || m.MaterialCode.Contains(keyword)))
                .Select(m => m.Id).ToList();
            if (materialIds.Count == 0) return new PagedResult<StockSummaryDto>();
        }

        var db = ledger.Db;
        var opening = await GroupedAsync(db, l => l.BizTime < begin, warehouseId, materialIds, sumOnly: true);
        var inbound = await GroupedAsync(db, l => l.BizTime >= begin && l.BizTime < endExclusive && l.ChangeQty > 0,
            warehouseId, materialIds, sumOnly: false);
        var outbound = await GroupedAsync(db, l => l.BizTime >= begin && l.BizTime < endExclusive && l.ChangeQty < 0,
            warehouseId, materialIds, sumOnly: false);
        var entries = await GroupedAsync(db, l => l.BizTime >= begin && l.BizTime < endExclusive,
            warehouseId, materialIds, sumOnly: false);

        var keys = new HashSet<(long, long)>();
        foreach (var list in new[] { opening, inbound, outbound, entries })
            foreach (var a in list) keys.Add((a.WarehouseId, a.MaterialId));

        var whNames = (await warehouses.ListAsync()).ToDictionary(w => w.Id, w => w.WarehouseName);
        var mats = (await materials.ListAsync()).ToDictionary(m => m.Id);

        var rows = new List<StockSummaryDto>();
        foreach (var (wh, mat) in keys)
        {
            var open = Sum(opening, wh, mat);
            var count = Count(entries, wh, mat);
            if (count == 0 && open == 0) continue; // 区间内没动过、期初也是零：不该占一行

            var dto = new StockSummaryDto
            {
                WarehouseId = wh.ToString(),
                WarehouseName = whNames.TryGetValue(wh, out var wn) ? wn : $"#{wh}",
                MaterialId = mat.ToString(),
                Opening = open,
                Inbound = Sum(inbound, wh, mat),
                Outbound = -Sum(outbound, wh, mat)
            };
            dto.Closing = dto.Opening + dto.Inbound - dto.Outbound;
            dto.Entries = count;
            if (mats.TryGetValue(mat, out var m))
            {
                dto.MaterialCode = m.MaterialCode;
                dto.MaterialName = m.MaterialName;
                dto.Spec = m.Spec;
                dto.Unit = m.Unit;
            }
            rows.Add(dto);
        }

        var sorted = rows.OrderBy(r => r.MaterialCode, StringComparer.Ordinal)
            .ThenBy(r => r.WarehouseName, StringComparer.Ordinal).ToList();
        return new PagedResult<StockSummaryDto>
        {
            Total = sorted.Count,
            Rows = sorted.Skip((query.PageNum - 1) * query.PageSize).Take(query.PageSize).ToList()
        };
    }

    private static async Task<List<Agg>> GroupedAsync(ISqlSugarClient db,
        System.Linq.Expressions.Expression<Func<ScmStockLedger, bool>> where, long? warehouseId,
        List<long>? materialIds, bool sumOnly)
        => await db.Queryable<ScmStockLedger>()
            .Where(where)
            .WhereIF(warehouseId is not null, l => l.WarehouseId == warehouseId!.Value)
            .WhereIF(materialIds is not null, l => materialIds!.Contains(l.MaterialId))
            .GroupBy(l => new { l.WarehouseId, l.MaterialId })
            .Select(l => new Agg
            {
                WarehouseId = l.WarehouseId, MaterialId = l.MaterialId, Qty = SqlFunc.AggregateSum(l.ChangeQty),
                Cnt = sumOnly ? 0 : SqlFunc.AggregateCount(l.Id)
            })
            .ToListAsync();

    private static decimal Sum(List<Agg> rows, long warehouseId, long materialId)
        => rows.FirstOrDefault(r => r.WarehouseId == warehouseId && r.MaterialId == materialId)?.Qty ?? 0m;

    private static int Count(List<Agg> rows, long warehouseId, long materialId)
        => rows.FirstOrDefault(r => r.WarehouseId == warehouseId && r.MaterialId == materialId)?.Cnt ?? 0;
}
