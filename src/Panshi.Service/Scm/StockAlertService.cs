using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;

namespace Panshi.Service.Scm;

/// <summary>
/// 库存预警：把「启用仓库 × 设了阈值的物料」全展开，对照 scm_stock 现存量判档。
/// 阈值挂在物料而不是仓库——同一种料在各仓通常按同一条安全库存管，且省掉一张冗余表与它在过账路径上的同步维护。
/// 数据量是主数据级（几十仓 × 几百料），内存算比拼一条跨表 SQL 好读；没有台账行按 0 存量算，
/// 否则「从没入过库的新料」永远不出现在预警里，而那恰恰是最该被看见的。
/// </summary>
public class StockAlertService(
    IRepository<MdMaterial> materials,
    IRepository<MdWarehouse> warehouses,
    IRepository<ScmStock> stocks)
{
    public async Task<PagedResult<StockAlertDto>> PageAsync(StockAlertQuery query)
    {
        var watched = (await materials.ListAsync(m => m.Status == EnableStatus.Enabled))
            .Where(m => m.MinStock is not null || m.MaxStock is not null)
            .ToList();
        var whs = await warehouses.ListAsync(w => w.Status == EnableStatus.Enabled);
        var qtyOf = (await stocks.ListAsync())
            .GroupBy(s => (s.WarehouseId, s.MaterialId))
            .ToDictionary(g => g.Key, g => g.Sum(s => s.Quantity));

        var keyword = query.Keyword?.Trim();
        long? onlyWarehouse = long.TryParse(query.WarehouseId, out var wid) && wid > 0 ? wid : null;

        var rows = new List<StockAlertDto>();
        foreach (var w in whs)
        {
            if (onlyWarehouse is { } filter && w.Id != filter) continue;
            foreach (var m in watched)
            {
                if (!string.IsNullOrEmpty(keyword)
                    && !m.MaterialName.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                    && !m.MaterialCode.Contains(keyword, StringComparison.OrdinalIgnoreCase)) continue;

                var qty = qtyOf.TryGetValue((w.Id, m.Id), out var v) ? v : 0m;
                var level = m.MinStock is { } min && qty < min ? StockAlertLevel.Short
                    : m.MaxStock is { } max && qty > max ? StockAlertLevel.Over
                    : (StockAlertLevel?)null;
                if (level is not { } hit) continue;
                if (query.Level is { } want && want != hit) continue;

                rows.Add(new StockAlertDto
                {
                    WarehouseId = w.Id.ToString(), WarehouseName = w.WarehouseName, MaterialId = m.Id.ToString(),
                    MaterialCode = m.MaterialCode, MaterialName = m.MaterialName, Spec = m.Spec, Unit = m.Unit,
                    MinStock = m.MinStock, MaxStock = m.MaxStock, Quantity = qty, Level = hit,
                    Gap = hit == StockAlertLevel.Short ? m.MinStock!.Value - qty : qty - m.MaxStock!.Value
                });
            }
        }

        // 缺口/超出量最大的排前面：一屏之内先看到最急的
        var sorted = rows.OrderByDescending(r => r.Gap).ThenBy(r => r.MaterialCode, StringComparer.Ordinal)
            .ThenBy(r => r.WarehouseName, StringComparer.Ordinal).ToList();
        return new PagedResult<StockAlertDto>
        {
            Total = sorted.Count,
            Rows = sorted.Skip((query.PageNum - 1) * query.PageSize).Take(query.PageSize).ToList()
        };
    }
}
