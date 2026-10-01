using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Service.Scm;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 库存过账语义。这里钉的是「台账 = 流水累计和」这条不变量在六种单据下都成立：
/// 不足要整单回滚（不能留下改了台账没记流水的半截账）、调拨双边、盘点按差额、作废按流水反向而不是按当前明细重算。
/// </summary>
[Collection("pg")]
public class StockPostingTests(PgFixture fx) : PgTestBase(fx), IAsyncLifetime
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private StockDocService Docs() => Fx.StockDocs();

    private readonly List<long> _warehouseIds = [];
    private readonly List<long> _materialIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// 测试库与开发库共用一个容器，建了不清会一路堆积——
    /// 之前「种子数据断言翻不到 WH-ZC」就是被这些残留挤掉的。
    /// 单据是软删的，取 id 时要 ClearFilter，否则删掉的草稿会留下孤行。
    /// </summary>
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

    /// <summary>建一个专属仓库 + 物料，避免与种子数据/其它用例抢同一行台账。</summary>
    private async Task<(string WarehouseId, string WarehouseB, string Material, string Material2)> FixtureAsync()
    {
        var tag = Tag();
        var whA = await Fx.Warehouses().CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "W" + tag + "A", WarehouseName = $"甲仓 {tag}" });
        var whB = await Fx.Warehouses().CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "W" + tag + "B", WarehouseName = $"乙仓 {tag}" });
        var m1 = await Fx.Materials().CreateAsync(
            new MaterialSaveDto { MaterialCode = "MT" + tag + "1", MaterialName = $"轴承 {tag}", Unit = "pcs" });
        var m2 = await Fx.Materials().CreateAsync(
            new MaterialSaveDto { MaterialCode = "MT" + tag + "2", MaterialName = "密封垫", Unit = "pcs" });
        _warehouseIds.AddRange([long.Parse(whA.Id), long.Parse(whB.Id)]);
        _materialIds.AddRange([long.Parse(m1.Id), long.Parse(m2.Id)]);
        return (whA.Id, whB.Id, m1.Id, m2.Id);
    }

    private static StockDocSaveDto Doc(StockDocKind kind, string warehouseId, string materialId, decimal qty,
        string? targetId = null)
        => new()
        {
            Kind = kind, WarehouseId = warehouseId, TargetWarehouseId = targetId, BizDate = DateTime.Today,
            Lines = [new StockDocLineSaveDto { MaterialId = materialId, Quantity = qty }]
        };

    private async Task<StockDto> StockRowAsync(string warehouseId, string materialId)
        => (await Fx.Stocks().PageAsync(new StockQuery { WarehouseId = warehouseId, PageSize = 200 }))
            .Rows.First(s => s.MaterialId == materialId);

    private async Task<decimal> QtyAsync(string warehouseId, string materialId)
    {
        var rows = (await Fx.Stocks().PageAsync(new StockQuery { WarehouseId = warehouseId, PageSize = 200 })).Rows;
        return rows.FirstOrDefault(s => s.MaterialId == materialId)?.Quantity ?? 0m;
    }

    private async Task<IReadOnlyList<LedgerDto>> LedgerAsync(string materialId)
        => (await Fx.Ledgers().PageAsync(new LedgerQuery { MaterialId = materialId, PageSize = 200 })).Rows;

    [Fact]
    public async Task Purchase_In_Posts_To_Stock_And_Writes_Ledger_Then_Locks()
    {
        var (wh, _, mat, _) = await FixtureAsync();
        var svc = Docs();
        var created = await svc.CreateAsync(Doc(StockDocKind.PurchaseIn, wh, mat, 12), 1, "admin", 2);
        Assert.StartsWith("RK", created.DocNo);
        Assert.Equal(StockDocStatus.Draft, created.Status);
        // 草稿不许碰库存
        Assert.Equal(0m, await QtyAsync(wh, mat));

        var posted = await svc.PostAsync(long.Parse(created.Id), 1, "admin");
        Assert.Equal(StockDocStatus.Posted, posted.Status);
        Assert.NotNull(posted.PostedTime);
        Assert.Equal(12m, await QtyAsync(wh, mat));
        Assert.NotNull((await StockRowAsync(wh, mat)).UpdateTime); // 首次入库也要有「最后变动」

        var ledger = await LedgerAsync(mat);
        Assert.Single(ledger);
        Assert.Equal(12m, ledger[0].ChangeQty);
        Assert.Equal(0m, ledger[0].BeforeQty);
        Assert.Equal(12m, ledger[0].AfterQty);
        Assert.Equal("admin", ledger[0].OperatorName);

        // 哨兵：把最后变动按回 2000 年再走一次更新路径。UpdateTime 不在 UpdateColumns 清单里就会原样留着
        var sentinel = new DateTime(2000, 1, 1);
        await Db.Updateable<ScmStock>().SetColumns(s => s.UpdateTime == sentinel)
            .Where(s => s.WarehouseId == long.Parse(wh) && s.MaterialId == long.Parse(mat)).ExecuteCommandAsync();
        await svc.PostAsync(long.Parse((await svc.CreateAsync(
            Doc(StockDocKind.PurchaseIn, wh, mat, 3), 1, "admin", 2)).Id), 1, "admin");
        Assert.True((await StockRowAsync(wh, mat)).UpdateTime > sentinel);

        // 已过账：不能重复过账、不能改、不能删
        await Assert.ThrowsAsync<BizException>(() => svc.PostAsync(long.Parse(created.Id), 1, "admin"));
        await Assert.ThrowsAsync<BizException>(() => svc.UpdateAsync(long.Parse(created.Id),
            Doc(StockDocKind.PurchaseIn, wh, mat, 99), 1));
        await Assert.ThrowsAsync<BizException>(() => svc.DeleteAsync(long.Parse(created.Id), 1));
    }

    [Fact]
    public async Task Sales_Out_Rejects_Oversell_And_Leaves_No_Half_Posted_Trace()
    {
        var (wh, _, mat, mat2) = await FixtureAsync();
        var svc = Docs();
        await svc.PostAsync(long.Parse((await svc.CreateAsync(
            Doc(StockDocKind.PurchaseIn, wh, mat, 5), 1, "admin", 2)).Id), 1, "admin");
        await svc.PostAsync(long.Parse((await svc.CreateAsync(
            Doc(StockDocKind.PurchaseIn, wh, mat2, 3), 1, "admin", 2)).Id), 1, "admin");

        // 一行能出（3）、后一行超卖（10 > 5）：整单必须回滚，先动的那行也不能留下痕迹
        var oversell = Doc(StockDocKind.SalesOut, wh, mat, 3);
        oversell.Lines.Add(new StockDocLineSaveDto { MaterialId = mat2, Quantity = 10 });
        var doc = await svc.CreateAsync(oversell, 1, "admin", 2);
        var denied = await Assert.ThrowsAsync<BizException>(() => svc.PostAsync(long.Parse(doc.Id), 1, "admin"));
        Assert.Contains("库存不足", denied.Message);

        Assert.Equal(5m, await QtyAsync(wh, mat));
        Assert.Equal(3m, await QtyAsync(wh, mat2));
        Assert.Equal(StockDocStatus.Draft, (await svc.GetAsync(long.Parse(doc.Id), 1)).Status);
        Assert.DoesNotContain(await LedgerAsync(mat), l => l.DocNo == doc.DocNo);
    }

    [Fact]
    public async Task Transfer_Moves_Both_Sides_And_Void_Reverses_Exact_Deltas()
    {
        var (whA, whB, mat, _) = await FixtureAsync();
        var svc = Docs();
        await svc.PostAsync(long.Parse((await svc.CreateAsync(
            Doc(StockDocKind.PurchaseIn, whA, mat, 10), 1, "admin", 2)).Id), 1, "admin");

        var transfer = await svc.PostAsync(
            long.Parse((await svc.CreateAsync(Doc(StockDocKind.Transfer, whA, mat, 4, whB), 1, "admin", 2)).Id),
            1, "admin");
        Assert.Equal(6m, await QtyAsync(whA, mat));
        Assert.Equal(4m, await QtyAsync(whB, mat));
        var ledger = await LedgerAsync(mat);
        Assert.Equal(3, ledger.Count); // 入库 1 条 + 调拨双边 2 条
        Assert.Contains(ledger, l => l.DocNo == transfer.DocNo && l.ChangeQty == -4m);
        Assert.Contains(ledger, l => l.DocNo == transfer.DocNo && l.ChangeQty == 4m);

        // 作废按流水反向：两仓回到 10 / 0
        await svc.VoidAsync(long.Parse(transfer.Id), 1, "admin");
        Assert.Equal(10m, await QtyAsync(whA, mat));
        Assert.Equal(0m, await QtyAsync(whB, mat));
        Assert.Equal(StockDocStatus.Void, (await svc.GetAsync(long.Parse(transfer.Id), 1)).Status);
    }

    [Fact]
    public async Task Count_Adjusts_By_Difference_And_Skips_Zero_Difference()
    {
        var (wh, _, mat, mat2) = await FixtureAsync();
        var svc = Docs();
        await svc.PostAsync(long.Parse((await svc.CreateAsync(
            Doc(StockDocKind.PurchaseIn, wh, mat, 8), 1, "admin", 2)).Id), 1, "admin");
        await svc.PostAsync(long.Parse((await svc.CreateAsync(
            Doc(StockDocKind.PurchaseIn, wh, mat2, 3), 1, "admin", 2)).Id), 1, "admin");

        // 实盘：mat 10（盘盈 +2）、mat2 3（无差异，不该产生流水）
        var count = Doc(StockDocKind.Count, wh, mat, 10);
        count.Lines.Add(new StockDocLineSaveDto { MaterialId = mat2, Quantity = 3 });
        var doc = await svc.CreateAsync(count, 1, "admin", 2);
        var detail = await svc.GetAsync(long.Parse(doc.Id), 1);
        Assert.Equal(8m, detail.Lines.First(l => l.MaterialId == mat).BookQty); // 盘点差异要看得见

        await svc.PostAsync(long.Parse(doc.Id), 1, "admin");
        Assert.Equal(10m, await QtyAsync(wh, mat));
        Assert.Equal(3m, await QtyAsync(wh, mat2));
        var moved = await LedgerAsync(mat);
        Assert.Contains(moved, l => l.DocNo == doc.DocNo && l.ChangeQty == 2m && l.BeforeQty == 8m && l.AfterQty == 10m);
        Assert.DoesNotContain(await LedgerAsync(mat2), l => l.DocNo == doc.DocNo);
    }

    [Fact]
    public async Task Stock_Doc_Validates_Warehouse_Rules_And_Line_Duplicates()
    {
        var (wh, whB, mat, _) = await FixtureAsync();
        var svc = Docs();

        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            Doc(StockDocKind.Transfer, wh, mat, 1), 1, "admin", 2)); // 调拨缺目标仓
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            Doc(StockDocKind.Transfer, wh, mat, 1, wh), 1, "admin", 2)); // 源=目标
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            Doc(StockDocKind.PurchaseIn, wh, mat, 1, whB), 1, "admin", 2)); // 非调拨带目标仓
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            Doc(StockDocKind.PurchaseIn, wh, mat, 0), 1, "admin", 2)); // 入库数量为 0
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            new StockDocSaveDto { Kind = StockDocKind.PurchaseIn, WarehouseId = wh, Lines = [] }, 1, "admin", 2));

        var dup = Doc(StockDocKind.PurchaseIn, wh, mat, 1);
        dup.Lines.Add(new StockDocLineSaveDto { MaterialId = mat, Quantity = 2 });
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(dup, 1, "admin", 2));

        // 盘点可以填 0（整批盘亏到零）
        var zero = await svc.CreateAsync(Doc(StockDocKind.Count, wh, mat, 0), 1, "admin", 2);
        Assert.Equal(StockDocStatus.Draft, zero.Status);
        await svc.DeleteAsync(long.Parse(zero.Id), 1);
    }

    [Fact]
    public async Task Stock_Doc_Page_Filters_By_Kind_Warehouse_And_Mine()
    {
        var (wh, whB, mat, _) = await FixtureAsync();
        var svc = Docs();
        var a = await svc.CreateAsync(Doc(StockDocKind.PurchaseIn, wh, mat, 5), 1, "admin", 2);
        var b = await svc.CreateAsync(Doc(StockDocKind.SalesOut, whB, mat, 1), 1, "admin", 2);
        try
        {
            Assert.Contains((await svc.PageAsync(new StockDocQuery { Kind = StockDocKind.PurchaseIn, Keyword = a.DocNo, PageSize = 50 }, 1)).Rows,
                r => r.Id == a.Id);
            Assert.DoesNotContain((await svc.PageAsync(new StockDocQuery { Kind = StockDocKind.PurchaseIn, Keyword = b.DocNo, PageSize = 50 }, 1)).Rows,
                r => r.Id == b.Id);
            var byWarehouse = await svc.PageAsync(new StockDocQuery { WarehouseId = whB, PageSize = 50 }, 1);
            Assert.All(byWarehouse.Rows, r => Assert.Equal(whB, r.WarehouseId));
            Assert.True((await svc.PageAsync(new StockDocQuery { Mine = true, PageSize = 50 }, 1)).Total >= 2);
            Assert.Equal(0, (await svc.PageAsync(new StockDocQuery { Mine = true, Begin = DateTime.Today.AddDays(1), PageSize = 50 }, 1)).Total);
        }
        finally
        {
            await svc.DeleteAsync(long.Parse(a.Id), 1);
            await svc.DeleteAsync(long.Parse(b.Id), 1);
        }
    }
}
