using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Service.Scm;
using SqlSugar;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 并发写路径回归——压测与底座盘点打出来的三类缺陷钉在这里：
/// ① 单号用 COUNT+1 生成，并发撞唯一索引直接 500；② 过账是 read-modify-write，并发丢更新，
/// 破坏「台账 = 流水累计和」，顺带让超卖检查不可信；③ 过账/作废先读状态判一下再裸写，
/// 同一张单被并发点两次就记两遍账。
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

    /// <summary>
    /// 并发风暴：所有分支卡在闸门上，一起起跑。
    /// ⚠️ 竞态窗口必须自己造出来——靠线程池的偶然重叠，用例会退化成「大多数时候绿」的空跑
    /// （作废出库单那一条就是这样：不加闸门时 20 个分支被串行化，摘掉守卫也不红）。
    /// </summary>
    private static async Task<bool[]> StormAsync(Func<Task> action, int n = 20)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var shots = Enumerable.Range(0, n).Select(async _ =>
        {
            await gate.Task;
            try
            {
                await action();
                return true;
            }
            catch (BizException)
            {
                return false;
            }
        }).ToArray();
        gate.SetResult();
        return await Task.WhenAll(shots);
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

    /// <summary>
    /// 同一张草稿单被并发点 20 次过账：状态流转必须是原子的，只允许一次成功。
    /// 旧写法（先读判 Draft、再按主键写 Posted）会让多个分支双双通过判断，库存记两遍。
    /// </summary>
    [Fact]
    public async Task Concurrent_Post_Same_Doc_Moves_Stock_Only_Once()
    {
        var (wh, mat) = await FixtureAsync();
        var id = long.Parse((await FreshDocs().CreateAsync(Doc(StockDocKind.PurchaseIn, wh, mat, 100), 1, "admin", 2)).Id);
        _docIds.Add(id);

        var posted = await StormAsync(async () => await FreshDocs().PostAsync(id, 1, "admin"));

        Assert.Equal(1, posted.Count(p => p));
        Assert.Equal(100m, await QtyAsync(wh, mat)); // 记两遍就是 200
        Assert.Single(await LedgerAsync(mat)); // 一张单只该留一条过账流水
    }

    /// <summary>
    /// 并发作废同一张<b>出库</b>单：作废是往回加库存，那个方向没有可用量守卫，
    /// 状态机是唯一防线——所以这里不能用入库单（会被上一轮修的原子扣减顺手挡掉，用例就成了空跑）。
    /// </summary>
    [Fact]
    public async Task Concurrent_Void_Same_Doc_Reverses_Only_Once()
    {
        var (wh, mat) = await FixtureAsync();
        var seed = FreshDocs();
        var seedId = long.Parse((await seed.CreateAsync(Doc(StockDocKind.PurchaseIn, wh, mat, 30), 1, "admin", 2)).Id);
        _docIds.Add(seedId);
        await seed.PostAsync(seedId, 1, "admin");

        var svc = FreshDocs();
        var id = long.Parse((await svc.CreateAsync(Doc(StockDocKind.SalesOut, wh, mat, 30), 1, "admin", 2)).Id);
        _docIds.Add(id);
        await svc.PostAsync(id, 1, "admin");
        Assert.Equal(0m, await QtyAsync(wh, mat));

        var voided = await StormAsync(async () => await FreshDocs().VoidAsync(id, 1, "admin"));

        Assert.Equal(1, voided.Count(v => v));
        Assert.Equal(30m, await QtyAsync(wh, mat)); // 冲两遍就是 60
        Assert.Equal(3, (await LedgerAsync(mat)).Count); // 入库 + 出库 + 冲销，各一条
    }
}
