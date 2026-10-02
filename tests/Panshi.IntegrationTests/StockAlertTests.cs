using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 库存预警判档。钉的是三件事：没设阈值的物料不该出现、没有台账行要按 0 存量算（否则从没入库的新料永远不报警）、
/// 以及缺口/超出量要按「最急的在前」排。
/// </summary>
[Collection("pg")]
public class StockAlertTests(PgFixture fx) : PgTestBase(fx), IAsyncLifetime
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private readonly List<long> _warehouseIds = [];
    private readonly List<long> _materialIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>测试库与开发库共用容器，建了不清会一路堆积（分页可达性断言就是这么翻过车）。</summary>
    public async Task DisposeAsync()
    {
        if (_warehouseIds.Count == 0 && _materialIds.Count == 0) return;

        var docIds = await Db.Queryable<ScmStockDoc>().ClearFilter()
            .Where(d => _warehouseIds.Contains(d.WarehouseId)).Select(d => d.Id).ToListAsync();
        if (docIds.Count > 0)
        {
            await Db.Deleteable<ScmStockDocLine>().Where(l => docIds.Contains(l.DocId)).ExecuteCommandAsync();
            await Db.Deleteable<ScmStockDoc>().In(docIds).ExecuteCommandAsync();
        }
        await Db.Deleteable<ScmStockDocLine>().Where(l => _materialIds.Contains(l.MaterialId)).ExecuteCommandAsync();
        await Db.Deleteable<ScmStockLedger>().Where(l => _materialIds.Contains(l.MaterialId)).ExecuteCommandAsync();
        await Db.Deleteable<ScmStock>().Where(s => _materialIds.Contains(s.MaterialId)).ExecuteCommandAsync();
        await Db.Deleteable<MdWarehouse>().In(_warehouseIds).ExecuteCommandAsync();
        await Db.Deleteable<MdMaterial>().In(_materialIds).ExecuteCommandAsync();
    }

    private async Task<string> NewWarehouseAsync(string tag)
    {
        var w = await Fx.Warehouses().CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "W" + tag + "A", WarehouseName = $"预警甲仓 {tag}" });
        _warehouseIds.Add(long.Parse(w.Id));
        return w.Id;
    }

    private async Task<string> NewMaterialAsync(string tag, string suffix, decimal? min, decimal? max)
    {
        var m = await Fx.Materials().CreateAsync(new MaterialSaveDto
        {
            MaterialCode = "AL" + tag + suffix, MaterialName = $"预警料 {tag}{suffix}", Unit = "pcs",
            MinStock = min, MaxStock = max
        });
        _materialIds.Add(long.Parse(m.Id));
        return m.Id;
    }

    private async Task PostInAsync(string warehouseId, string materialId, decimal qty)
    {
        var doc = await Fx.StockDocs().CreateAsync(new StockDocSaveDto
        {
            Kind = StockDocKind.PurchaseIn, WarehouseId = warehouseId, BizDate = DateTime.Today,
            Lines = [new StockDocLineSaveDto { MaterialId = materialId, Quantity = qty }]
        }, 1, "admin", 2);
        await Fx.StockDocs().PostAsync(long.Parse(doc.Id), 1, "admin");
    }

    private async Task<IReadOnlyList<StockAlertDto>> AlertsAsync(string keyword, string? warehouseId = null,
        StockAlertLevel? level = null)
            => (await Fx.StockAlerts().PageAsync(new StockAlertQuery
            {
                Keyword = keyword, WarehouseId = warehouseId, Level = level, PageSize = 200
            })).Rows;

    [Fact]
    public async Task Alert_Flags_Missing_Stock_Row_As_Zero_And_Ignores_Unthresholded_Material()
    {
        var tag = Tag();
        var wh = await NewWarehouseAsync(tag);
        var withMin = await NewMaterialAsync(tag, "1", 10, 100);
        var plain = await NewMaterialAsync(tag, "2", null, null);

        // 一次库存都没入过：没有台账行也该报「缺货」，缺口=下限
        var rows = await AlertsAsync("AL" + tag);
        var mine = rows.Where(r => r.WarehouseId == wh).ToList();
        Assert.Single(mine);
        Assert.Equal(withMin, mine[0].MaterialId);
        Assert.Equal(0m, mine[0].Quantity);
        Assert.Equal(StockAlertLevel.Short, mine[0].Level);
        Assert.Equal(10m, mine[0].Gap);
        Assert.DoesNotContain(mine, r => r.MaterialId == plain);
    }

    [Fact]
    public async Task Alert_Leaves_Window_Alone_And_Flags_Overstock()
    {
        var tag = Tag();
        var wh = await NewWarehouseAsync(tag);
        var mat = await NewMaterialAsync(tag, "1", 10, 100);

        await PostInAsync(wh, mat, 30);
        var inWindow = await AlertsAsync("AL" + tag);
        Assert.DoesNotContain(inWindow, r => r.WarehouseId == wh); // 10 ≤ 30 ≤ 100

        await PostInAsync(wh, mat, 90); // 120 > 100
        var over = (await AlertsAsync("AL" + tag)).Single(r => r.WarehouseId == wh);
        Assert.Equal(StockAlertLevel.Over, over.Level);
        Assert.Equal(120m, over.Quantity);
        Assert.Equal(20m, over.Gap);
    }

    [Fact]
    public async Task Alert_Filters_By_Level_Warehouse_And_Orders_By_Urgency()
    {
        var tag = Tag();
        var whA = await NewWarehouseAsync(tag);
        var whB = (await Fx.Warehouses().CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "W" + tag + "B", WarehouseName = $"预警乙仓 {tag}" })).Id;
        _warehouseIds.Add(long.Parse(whB));
        var little = await NewMaterialAsync(tag, "1", 5, null);   // 缺 5
        var lots = await NewMaterialAsync(tag, "2", 500, null);   // 缺 500

        var all = (await AlertsAsync("AL" + tag)).Where(r => r.WarehouseId == whA).ToList();
        Assert.Equal(2, all.Count);
        Assert.Equal(lots, all[0].MaterialId); // 缺口大的排前面
        Assert.All(all, r => Assert.Equal(StockAlertLevel.Short, r.Level));

        var onlyB = await AlertsAsync("AL" + tag, whB);
        Assert.All(onlyB, r => Assert.Equal(whB, r.WarehouseId));
        Assert.Equal(2, onlyB.Count);

        Assert.Empty(await AlertsAsync("AL" + tag, whA, StockAlertLevel.Over));
        Assert.Equal(2, (await AlertsAsync("AL" + tag, whA, StockAlertLevel.Short)).Count);
    }

    [Fact]
    public async Task Material_Rejects_Inconsistent_Thresholds()
    {
        var tag = Tag();
        await Assert.ThrowsAsync<BizException>(() => Fx.Materials().CreateAsync(new MaterialSaveDto
        {
            MaterialCode = "AL" + tag + "X", MaterialName = "上限不大于下限", MinStock = 100, MaxStock = 100
        }));
        await Assert.ThrowsAsync<BizException>(() => Fx.Materials().CreateAsync(new MaterialSaveDto
        {
            MaterialCode = "AL" + tag + "Y", MaterialName = "负下限", MinStock = -1
        }));

        var ok = await Fx.Materials().CreateAsync(new MaterialSaveDto
        {
            MaterialCode = "AL" + tag + "Z", MaterialName = "只设下限", MinStock = 10
        });
        _materialIds.Add(long.Parse(ok.Id));
        Assert.Equal(10m, ok.MinStock);
        Assert.Null(ok.MaxStock);

        // 改阈值走乐观锁同一条路径，也要能清成「不预警」
        await Fx.Materials().UpdateAsync(long.Parse(ok.Id), new MaterialSaveDto
        {
            MaterialCode = ok.MaterialCode, MaterialName = ok.MaterialName, Unit = ok.Unit,
            MinStock = null, MaxStock = null, Status = ok.Status, Version = ok.Version
        });
        var after = (await Fx.Materials().PageAsync(new MaterialQuery { Keyword = ok.MaterialCode, PageSize = 5 }))
            .Rows.Single(r => r.Id == ok.Id);
        Assert.Null(after.MinStock);
        Assert.Empty(await AlertsAsync("AL" + tag));
    }
}
