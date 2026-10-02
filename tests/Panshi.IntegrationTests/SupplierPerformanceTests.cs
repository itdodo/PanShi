using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 供应商绩效。钉的是三个容易做错的口径：没回填来源单号的入库会让订单看起来「未到货」，
/// 没填计划交期的订单不能进准交率分母（否则等于替供应商编了个按期率），准交率无分母时返回 null 而不是 0。
/// </summary>
[Collection("pg")]
public class SupplierPerformanceTests(PgFixture fx) : PgTestBase(fx), IAsyncLifetime
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

    private async Task<(string Supplier, string Name)> NewSupplierAsync(string tag)
    {
        var s = await Fx.Suppliers().CreateAsync(new SupplierSaveDto
        {
            SupplierCode = "PF" + tag, SupplierName = $"绩效供应商 {tag}"
        });
        _supplierIds.Add(long.Parse(s.Id));
        return (s.Id, s.SupplierName);
    }

    private async Task<string> NewOrderAsync(string supplierId, string materialId, DateTime orderDate, DateTime? deliveryDate)
    {
        var o = await Fx.PurchaseOrders().CreateAsync(new PurchaseOrderSaveDto
        {
            SupplierId = supplierId, OrderDate = orderDate, DeliveryDate = deliveryDate,
            Lines = [new OrderLineSaveDto { MaterialId = materialId, Quantity = 10, UnitPrice = 100, TaxRate = 13 }]
        }, 1, "admin", 2);
        _orderIds.Add(long.Parse(o.Id));
        // 直接置为已通过：绩效只统计已批准的单，走审批流会把用例变成流程测试
        await Db.Updateable<ScmPurchaseOrder>().SetColumns(x => x.Status == BizDocStatus.Approved)
            .Where(x => x.Id == long.Parse(o.Id)).ExecuteCommandAsync();
        return o.DocNo;
    }

    private async Task ReceiveAsync(string warehouseId, string materialId, string sourceOrderNo)
    {
        var doc = await Fx.StockDocs().CreateAsync(new StockDocSaveDto
        {
            Kind = StockDocKind.PurchaseIn, WarehouseId = warehouseId, BizDate = DateTime.Today,
            SourceOrderNo = sourceOrderNo, Lines = [new StockDocLineSaveDto { MaterialId = materialId, Quantity = 10 }]
        }, 1, "admin", 2);
        _docIds.Add(long.Parse(doc.Id));
        await Fx.StockDocs().PostAsync(long.Parse(doc.Id), 1, "admin");
    }

    [Fact]
    public async Task Performance_Counts_OnTime_Late_Pending_And_Ignores_Unplanned_In_Denominator()
    {
        var tag = Tag();
        var today = DateTime.Today;
        var wh = await Fx.Warehouses().CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "P" + tag + "W", WarehouseName = $"绩效仓 {tag}" });
        _warehouseIds.Add(long.Parse(wh.Id));
        var mat = await Fx.Materials().CreateAsync(
            new MaterialSaveDto { MaterialCode = "PF" + tag + "M", MaterialName = $"绩效料 {tag}", Unit = "pcs" });
        _materialIds.Add(long.Parse(mat.Id));
        var (sup, name) = await NewSupplierAsync(tag);

        var late = await NewOrderAsync(sup, mat.Id, today.AddDays(-20), today.AddDays(-10));   // 迟到，交付 20 天
        var onTime = await NewOrderAsync(sup, mat.Id, today.AddDays(-20), today.AddDays(10));  // 按期，交付 20 天
        await NewOrderAsync(sup, mat.Id, today.AddDays(-5), today.AddDays(5));                 // 未到货
        var unplanned = await NewOrderAsync(sup, mat.Id, today.AddDays(-30), null);            // 没填交期，不进分母
        await ReceiveAsync(wh.Id, mat.Id, late);
        await ReceiveAsync(wh.Id, mat.Id, onTime);
        await ReceiveAsync(wh.Id, mat.Id, unplanned);

        var row = (await Fx.SupplierPerf().PageAsync(new SupplierPerformanceQuery { PageSize = 100 }))
            .Rows.Single(r => r.SupplierId == sup);
        Assert.Equal(name, row.SupplierName);
        Assert.Equal(4, row.Orders);
        Assert.Equal(3, row.Delivered);
        Assert.Equal(1, row.Pending);
        Assert.Equal(2, row.OnTimeBase); // 没填计划交期的那条不参与准交率
        Assert.Equal(1, row.OnTime);
        Assert.Equal(0.5m, row.OnTimeRate);
        Assert.Equal(23.3m, row.AvgLeadDays); // (20 + 20 + 30) / 3
        Assert.Equal(4000m, row.Amount);      // 4 张单 × 10×100
    }

    [Fact]
    public async Task Performance_Honours_Range_Keyword_And_Null_Rate_Without_Plan()
    {
        var tag = Tag();
        var today = DateTime.Today;
        var wh = await Fx.Warehouses().CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "Q" + tag + "W", WarehouseName = $"绩效乙仓 {tag}" });
        _warehouseIds.Add(long.Parse(wh.Id));
        var mat = await Fx.Materials().CreateAsync(
            new MaterialSaveDto { MaterialCode = "PF" + tag + "M", MaterialName = $"绩效料 {tag}", Unit = "pcs" });
        _materialIds.Add(long.Parse(mat.Id));
        var (sup, _) = await NewSupplierAsync(tag);

        var old = await NewOrderAsync(sup, mat.Id, today.AddYears(-2), today.AddYears(-2).AddDays(7));
        var fresh = await NewOrderAsync(sup, mat.Id, today, today.AddDays(7));
        await ReceiveAsync(wh.Id, mat.Id, old);

        var recent = (await Fx.SupplierPerf().PageAsync(new SupplierPerformanceQuery
        {
            Begin = today.AddMonths(-1), End = today, Keyword = "绩效供应商 " + tag, PageSize = 100
        })).Rows.Single(r => r.SupplierId == sup);
        Assert.Equal(1, recent.Orders);   // 两年前的单不在区间内
        Assert.Equal(0, recent.Delivered);
        Assert.Equal(1, recent.Pending);
        Assert.Null(recent.OnTimeRate);   // 没有已到货且填了交期的订单 → 不编造比率
        Assert.Equal(0m, recent.AvgLeadDays);

        await Assert.ThrowsAsync<BizException>(() => Fx.SupplierPerf().PageAsync(new SupplierPerformanceQuery
        {
            Begin = today, End = today.AddDays(-1)
        }));
    }
}
