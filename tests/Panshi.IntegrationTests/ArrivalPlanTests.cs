using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 到货计划。钉的是「计划只生成一次、收货量只从已过账的采购入库单推导」这两条：
/// 生成侧不能覆盖人工改过的日期，读数侧不能把草稿/作废的单算成已到货。
/// </summary>
[Collection("pg")]
public class ArrivalPlanTests(PgFixture fx) : PgTestBase(fx), IAsyncLifetime
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private readonly List<long> _orderIds = [];
    private readonly List<long> _docIds = [];
    private readonly List<long> _warehouseIds = [];
    private readonly List<long> _materialIds = [];
    private readonly List<long> _supplierIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_orderIds.Count == 0 && _docIds.Count == 0) return;

        await Db.Deleteable<ScmPurchaseArrival>().Where(a => _orderIds.Contains(a.OrderId)).ExecuteCommandAsync();
        if (_orderIds.Count > 0)
            await Db.Deleteable<ScmPurchaseOrderLine>().Where(l => _orderIds.Contains(l.OrderId)).ExecuteCommandAsync();
        await Db.Deleteable<ScmPurchaseOrder>().In(_orderIds).ExecuteCommandAsync();
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
        await Db.Deleteable<MdSupplier>().In(_supplierIds).ExecuteCommandAsync();
    }

    private async Task<(string Supplier, string Wh, string Mat1, string Mat2, string Code)> NewFixtureAsync()
    {
        var tag = Tag();
        var sup = await Fx.Suppliers().CreateAsync(new SupplierSaveDto
        {
            SupplierCode = "AP" + tag, SupplierName = $"到货供应商 {tag}"
        });
        var wh = await Fx.Warehouses().CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "AP" + tag + "W", WarehouseName = $"到货仓 {tag}" });
        var m1 = await Fx.Materials().CreateAsync(
            new MaterialSaveDto { MaterialCode = "AP" + tag + "1", MaterialName = $"到货料一 {tag}", Unit = "pcs" });
        var m2 = await Fx.Materials().CreateAsync(
            new MaterialSaveDto { MaterialCode = "AP" + tag + "2", MaterialName = $"到货料二 {tag}", Unit = "pcs" });
        _supplierIds.Add(long.Parse(sup.Id));
        _warehouseIds.Add(long.Parse(wh.Id));
        _materialIds.AddRange([long.Parse(m1.Id), long.Parse(m2.Id)]);
        return (sup.Id, wh.Id, m1.Id, m2.Id, "AP" + tag);
    }

    /// <summary>建两行订单并提交（库里这条表没绑流程 → 直通通过，也就走到「生成计划」那一步）。</summary>
    private async Task<(string Id, string DocNo)> NewApprovedOrderAsync(string supplierId, string mat1, string mat2,
        DateTime deliveryDate)
    {
        var o = await Fx.PurchaseOrders().CreateAsync(new PurchaseOrderSaveDto
        {
            SupplierId = supplierId, OrderDate = DateTime.Today.AddDays(-10), DeliveryDate = deliveryDate,
            Lines =
            [
                new OrderLineSaveDto { MaterialId = mat1, Quantity = 10, UnitPrice = 100, TaxRate = 13 },
                new OrderLineSaveDto { MaterialId = mat2, Quantity = 4, UnitPrice = 50, TaxRate = 13 }
            ]
        }, 1, "admin", 2);
        _orderIds.Add(long.Parse(o.Id));
        var submitted = await Fx.PurchaseOrders().SubmitAsync(long.Parse(o.Id), 1, "admin", new FlowSubmitDto
        {
            BusinessTable = "scm_purchase_order", BusinessId = long.Parse(o.Id), Variables = new()
        });
        Assert.Equal(BizDocStatus.Approved, submitted.Status);
        return (o.Id, submitted.DocNo);
    }

    private async Task ReceiveAsync(string warehouseId, string materialId, decimal qty, string? sourceOrderNo,
        StockDocStatus status = StockDocStatus.Posted)
    {
        var doc = await Fx.StockDocs().CreateAsync(new StockDocSaveDto
        {
            Kind = StockDocKind.PurchaseIn, WarehouseId = warehouseId, BizDate = DateTime.Today,
            SourceOrderNo = sourceOrderNo, Lines = [new StockDocLineSaveDto { MaterialId = materialId, Quantity = qty }]
        }, 1, "admin", 2);
        _docIds.Add(long.Parse(doc.Id));
        if (status == StockDocStatus.Posted) await Fx.StockDocs().PostAsync(long.Parse(doc.Id), 1, "admin");
    }

    private async Task<IReadOnlyList<ArrivalDto>> PlansAsync(string code, ArrivalQuery? over = null)
        => (await Fx.Arrivals().PageAsync(new ArrivalQuery
        {
            Keyword = over?.Keyword ?? code, SupplierId = over?.SupplierId, Status = over?.Status,
            OverdueOnly = over?.OverdueOnly ?? false, DueBefore = over?.DueBefore, Mine = over?.Mine ?? false,
            PageSize = 200
        }, 1)).Rows;

    [Fact]
    public async Task Approval_Generates_One_Line_Per_Order_Line_And_Is_Idempotent()
    {
        var (sup, _, m1, m2, code) = await NewFixtureAsync();
        var (orderId, docNo) = await NewApprovedOrderAsync(sup, m1, m2, DateTime.Today.AddDays(7));

        var rows = await PlansAsync(code);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(docNo, r.OrderNo);
            Assert.Equal(orderId, r.OrderId);
            Assert.Equal(DateTime.Today.AddDays(7).Date, r.PlanDate.Date);
            Assert.Equal(ArrivalStatus.Pending, r.Status);
            Assert.Equal(0m, r.ReceivedQty);
            Assert.False(r.Rescheduled);
        });
        Assert.Equal(10m, rows.Single(r => r.MaterialId == m1).PlanQty);
        Assert.Equal(4m, rows.Single(r => r.MaterialId == m2).PlanQty);

        // 再生成一次（模拟重复回调）：行数与计划日都不该动
        await Fx.Arrivals().GenerateForOrderAsync(long.Parse(orderId));
        Assert.Equal(2, (await PlansAsync(code)).Count);
    }

    [Fact]
    public async Task Draft_Order_Generates_Nothing()
    {
        var (sup, _, m1, m2, code) = await NewFixtureAsync();
        var o = await Fx.PurchaseOrders().CreateAsync(new PurchaseOrderSaveDto
        {
            SupplierId = sup, OrderDate = DateTime.Today, DeliveryDate = DateTime.Today.AddDays(5),
            Lines = [new OrderLineSaveDto { MaterialId = m1, Quantity = 3, UnitPrice = 10, TaxRate = 13 }]
        }, 1, "admin", 2);
        _orderIds.Add(long.Parse(o.Id));
        await Fx.Arrivals().GenerateForOrderAsync(long.Parse(o.Id));
        Assert.Empty(await PlansAsync(code));
    }

    [Fact]
    public async Task Received_Comes_From_Posted_Purchase_In_Only()
    {
        var (sup, wh, m1, m2, code) = await NewFixtureAsync();
        var (_, docNo) = await NewApprovedOrderAsync(sup, m1, m2, DateTime.Today.AddDays(7));

        await ReceiveAsync(wh, m1, 6, docNo);          // 已过账 6/10 → 部分
        await ReceiveAsync(wh, m2, 4, null);           // 没回链订单号 → 不该计入
        await ReceiveAsync(wh, m1, 4, docNo, StockDocStatus.Draft); // 草稿 → 不该计入

        var plan1 = (await PlansAsync(code)).Single(r => r.MaterialId == m1);
        Assert.Equal(6m, plan1.ReceivedQty);
        Assert.Equal(4m, plan1.OpenQty);
        Assert.Equal(ArrivalStatus.Partial, plan1.Status);
        var plan2 = (await PlansAsync(code)).Single(r => r.MaterialId == m2);
        Assert.Equal(ArrivalStatus.Pending, plan2.Status);

        await ReceiveAsync(wh, m1, 6, docNo); // 累计 12 > 10 → 收满且超收
        var over = (await PlansAsync(code)).Single(r => r.MaterialId == m1);
        Assert.Equal(12m, over.ReceivedQty);
        Assert.Equal(ArrivalStatus.Done, over.Status);
        Assert.Equal(-2m, over.OpenQty);
        Assert.False(over.Overdue);
    }

    [Fact]
    public async Task Overdue_Is_Derived_And_Reschedule_Survives_Regeneration()
    {
        var (sup, wh, m1, m2, code) = await NewFixtureAsync();
        var (orderId, _) = await NewApprovedOrderAsync(sup, m1, m2, DateTime.Today.AddDays(-3));

        var rows = await PlansAsync(code);
        Assert.All(rows, r => Assert.True(r.Overdue)); // 计划日已过且没收，两行都该是逾期
        Assert.Equal(2, (await PlansAsync(code, new ArrivalQuery { OverdueOnly = true })).Count);
        Assert.Equal(2, (await PlansAsync(code, new ArrivalQuery { Status = ArrivalStatus.Pending })).Count);
        Assert.Empty(await PlansAsync(code, new ArrivalQuery { Status = ArrivalStatus.Done }));

        var target = rows[0];
        await Fx.Arrivals().RescheduleAsync(long.Parse(target.Id), new ArrivalRescheduleDto
        {
            PlanDate = DateTime.Today.AddDays(9), Remark = "供应商改口了", Version = target.Version
        });

        var moved = (await PlansAsync(code)).Single(r => r.Id == target.Id);
        Assert.Equal(DateTime.Today.AddDays(9).Date, moved.PlanDate.Date);
        Assert.True(moved.Rescheduled);
        Assert.False(moved.Overdue);
        Assert.Equal("供应商改口了", moved.Remark);

        // 改期后只剩另一行还逾期
        var stillLate = await PlansAsync(code, new ArrivalQuery { OverdueOnly = true });
        Assert.Single(stillLate);
        Assert.NotEqual(target.Id, stillLate[0].Id);

        // 重新生成不得把人工改过的日期冲掉
        await Fx.Arrivals().GenerateForOrderAsync(long.Parse(orderId));
        Assert.Equal(DateTime.Today.AddDays(9).Date,
            (await PlansAsync(code)).Single(r => r.Id == target.Id).PlanDate.Date);

        // DueBefore 按整天含尾
        Assert.Single(await PlansAsync(code, new ArrivalQuery { DueBefore = DateTime.Today }));
    }
}
