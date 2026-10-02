using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Service.Scm;
using SqlSugar;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 并发写路径回归——压测（20 并发打 18080）打出来的两个缺陷钉在这里：
/// ① 单号用 COUNT+1 生成，并发撞唯一索引直接 500；② 过账是 read-modify-write，并发丢更新，
/// 破坏「台账 = 流水累计和」，顺带让超卖检查不可信。
/// 每个任务一套独立仓储：线上是一个请求一个 DI 作用域一条连接，共用 fixture 的 Db 测的是排队不是并发。
/// </summary>
[Collection("pg")]
public class ConcurrencyTests(PgFixture fx) : PgTestBase(fx), IAsyncLifetime
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private readonly List<long> _warehouseIds = [];
    private readonly List<long> _materialIds = [];
    private readonly List<long> _docIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_docIds.Count > 0)
        {
            await Db.Deleteable<ScmStockDocLine>().Where(l => _docIds.Contains(l.DocId)).ExecuteCommandAsync();
            await Db.Deleteable<ScmStockDoc>().In(_docIds).ExecuteCommandAsync();
        }
        if (_materialIds.Count > 0)
        {
            await Db.Deleteable<ScmStockLedger>().Where(l => _materialIds.Contains(l.MaterialId)).ExecuteCommandAsync();
            await Db.Deleteable<ScmStock>().Where(s => _materialIds.Contains(s.MaterialId)).ExecuteCommandAsync();
        }
        await Db.Deleteable<MdWarehouse>().In(_warehouseIds).ExecuteCommandAsync();
        await Db.Deleteable<MdMaterial>().In(_materialIds).ExecuteCommandAsync();
    }

    private async Task<(string Wh, string Mat)> FixtureAsync()
    {
        var tag = Tag();
        var wh = await Fx.Warehouses().CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "CX" + tag, WarehouseName = $"并发仓 {tag}" });
        var mat = await Fx.Materials().CreateAsync(
            new MaterialSaveDto { MaterialCode = "CX" + tag + "M", MaterialName = $"并发料 {tag}", Unit = "pcs" });
        _warehouseIds.Add(long.Parse(wh.Id));
        _materialIds.Add(long.Parse(mat.Id));
        return (wh.Id, mat.Id);
    }

    private static StockDocSaveDto Doc(StockDocKind kind, string wh, string mat, decimal qty) => new()
    {
        Kind = kind, WarehouseId = wh, BizDate = DateTime.Today,
        Lines = [new StockDocLineSaveDto { MaterialId = mat, Quantity = qty }]
    };

    /// <summary>每个并发分支各自的连接 + 各自的服务实例。</summary>
    private StockDocService FreshDocs()
    {
        ISqlSugarClient db = Fx.NewDb();
        return Fx.StockDocsOn(db);
    }

    private async Task<decimal> QtyAsync(string warehouseId, string materialId)
        => (await Fx.Stocks().PageAsync(new StockQuery { WarehouseId = warehouseId, PageSize = 200 })).Rows
            .FirstOrDefault(s => s.MaterialId == materialId)?.Quantity ?? 0m;

    private async Task<IReadOnlyList<LedgerDto>> LedgerAsync(string materialId)
        => (await Fx.Ledgers().PageAsync(new LedgerQuery { MaterialId = materialId, PageSize = 500 })).Rows;

    [Fact]
    public async Task Concurrent_Create_Allocates_Unique_DocNo()
    {
        var (wh, mat) = await FixtureAsync();

        var docNos = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            var svc = FreshDocs();
            var d = await svc.CreateAsync(Doc(StockDocKind.PurchaseIn, wh, mat, 1), 1, "admin", 2);
            lock (_docIds) _docIds.Add(long.Parse(d.Id));
            return d.DocNo;
        }));

        Assert.Equal(20, docNos.Length);
        Assert.Equal(20, docNos.Distinct().Count());
    }

    [Fact]
    public async Task Concurrent_Posting_Never_Oversells_And_Keeps_Ledger_Equal_To_Stock()
    {
        var (wh, mat) = await FixtureAsync();
        var seed = FreshDocs();
        var seedId = long.Parse((await seed.CreateAsync(Doc(StockDocKind.PurchaseIn, wh, mat, 50), 1, "admin", 2)).Id);
        _docIds.Add(seedId);
        await seed.PostAsync(seedId, 1, "admin");

        // 20 并发各出 5，库存只有 50：恰好 10 个能成，另外 10 个必须被「库存不足」挡住
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 20).Select(async i =>
        {
            var svc = FreshDocs();
            var id = long.Parse((await svc.CreateAsync(Doc(StockDocKind.SalesOut, wh, mat, 5), 1, "admin", 2)).Id);
            lock (_docIds) _docIds.Add(id);
            try
            {
                await svc.PostAsync(id, 1, "admin");
                return true;
            }
            catch (BizException)
            {
                return false;
            }
        }));

        Assert.Equal(10, outcomes.Count(o => o));
        Assert.Equal(10, outcomes.Count(o => !o));
        Assert.Equal(0m, await QtyAsync(wh, mat));
        // 核心不变量：台账现存量必须等于流水累计和（丢更新会让它偏大）
        Assert.Equal(await QtyAsync(wh, mat), (await LedgerAsync(mat)).Sum(l => l.ChangeQty));
    }
}
