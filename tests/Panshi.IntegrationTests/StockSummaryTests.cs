using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 进销存汇总。钉的是「期初 + 收入 − 发出 = 期末」这条恒等式在任意区间都成立——
/// 期初来自区间之前的流水，所以把区间整体挪到活动之前/之后，各列必须跟着换位置而不是简单清零。
/// </summary>
[Collection("pg")]
public class StockSummaryTests(PgFixture fx) : PgTestBase(fx), IAsyncLifetime
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private readonly List<long> _warehouseIds = [];
    private readonly List<long> _materialIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

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

    private async Task<(string Wh, string Mat)> FixtureAsync()
    {
        var tag = Tag();
        var wh = await Fx.Warehouses().CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "S" + tag + "A", WarehouseName = $"汇总甲仓 {tag}" });
        var mat = await Fx.Materials().CreateAsync(new MaterialSaveDto
        {
            MaterialCode = "SM" + tag, MaterialName = $"汇总料 {tag}", Unit = "pcs"
        });
        _warehouseIds.Add(long.Parse(wh.Id));
        _materialIds.Add(long.Parse(mat.Id));
        return (wh.Id, mat.Id);
    }

    private async Task PostAsync(StockDocKind kind, string wh, string mat, decimal qty)
    {
        var doc = await Fx.StockDocs().CreateAsync(new StockDocSaveDto
        {
            Kind = kind, WarehouseId = wh, BizDate = DateTime.Today,
            Lines = [new StockDocLineSaveDto { MaterialId = mat, Quantity = qty }]
        }, 1, "admin", 2);
        await Fx.StockDocs().PostAsync(long.Parse(doc.Id), 1, "admin");
    }

    private async Task<IReadOnlyList<StockSummaryDto>> SummaryAsync(string keyword, DateTime begin, DateTime? end = null,
        string? warehouseId = null)
            => (await Fx.StockSummary().PageAsync(new StockSummaryQuery
            {
                Keyword = keyword, Begin = begin, End = end ?? begin, WarehouseId = warehouseId, PageSize = 200
            })).Rows;

    [Fact]
    public async Task Summary_Balances_In_Out_And_Closing_For_Todays_Window()
    {
        var (wh, mat) = await FixtureAsync();
        var code = (await Fx.Materials().PageAsync(new MaterialQuery { Keyword = "SM", PageSize = 200 }))
            .Rows.First(m => m.Id == mat).MaterialCode;
        await PostAsync(StockDocKind.PurchaseIn, wh, mat, 12);
        await PostAsync(StockDocKind.SalesOut, wh, mat, 5);

        var row = (await SummaryAsync(code, DateTime.Today)).Single(r => r.WarehouseId == wh);
        Assert.Equal(0m, row.Opening);          // 今天之前没有流水
        Assert.Equal(12m, row.Inbound);
        Assert.Equal(5m, row.Outbound);
        Assert.Equal(7m, row.Closing);
        Assert.Equal(2, row.Entries);
        Assert.Equal(row.Opening + row.Inbound - row.Outbound, row.Closing);
        Assert.Equal("pcs", row.Unit);
    }

    [Fact]
    public async Task Summary_Window_After_Activity_Shows_Opening_With_No_Entries()
    {
        var (wh, mat) = await FixtureAsync();
        var code = (await Fx.Materials().PageAsync(new MaterialQuery { Keyword = "SM", PageSize = 200 }))
            .Rows.First(m => m.Id == mat).MaterialCode;
        await PostAsync(StockDocKind.PurchaseIn, wh, mat, 9);

        // 区间整体挪到活动之后：没有本期活动，但期初/期末要把历史带过来
        var tomorrow = DateTime.Today.AddDays(1);
        var row = (await SummaryAsync(code, tomorrow)).Single(r => r.WarehouseId == wh);
        Assert.Equal(9m, row.Opening);
        Assert.Equal(0m, row.Inbound);
        Assert.Equal(0m, row.Outbound);
        Assert.Equal(0, row.Entries);
        Assert.Equal(9m, row.Closing);

        // 区间整体挪到活动之前：既没期初也没活动，不该占一行
        Assert.DoesNotContain(await SummaryAsync(code, DateTime.Today.AddDays(-2)), r => r.WarehouseId == wh);
    }

    [Fact]
    public async Task Summary_Requires_Range_And_Filters_By_Warehouse()
    {
        var (wh, mat) = await FixtureAsync();
        var code = (await Fx.Materials().PageAsync(new MaterialQuery { Keyword = "SM", PageSize = 200 }))
            .Rows.First(m => m.Id == mat).MaterialCode;
        await PostAsync(StockDocKind.PurchaseIn, wh, mat, 4);

        await Assert.ThrowsAsync<BizException>(() => Fx.StockSummary().PageAsync(new StockSummaryQuery
        {
            Begin = null, PageSize = 20
        }));
        await Assert.ThrowsAsync<BizException>(() => Fx.StockSummary().PageAsync(new StockSummaryQuery
        {
            Begin = DateTime.Today, End = DateTime.Today.AddDays(-1), PageSize = 20
        }));

        var other = await Fx.Warehouses().CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "S" + Tag() + "B", WarehouseName = "汇总乙仓" });
        _warehouseIds.Add(long.Parse(other.Id));
        Assert.All(await SummaryAsync(code, DateTime.Today, null, other.Id), r => Assert.Equal(other.Id, r.WarehouseId));
        Assert.NotEmpty(await SummaryAsync(code, DateTime.Today, null, wh));
    }
}
