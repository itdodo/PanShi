using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 供应链订单与两张新基础资料的计算/校验口径。
/// 重点是金额：行金额与表头合计都由后端算，四舍五入口径一旦被改动，
/// 对账时才会发现单子合计和明细加起来差一分——所以这里用 100.005 / 99.99 这种带尾数的价。
/// </summary>
[Collection("pg")]
public class ScmOrderTests(PgFixture fx) : PgTestBase(fx)
{
    private static string Tag() => Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private async Task<(string SupplierId, string SupplierName, string MaterialA, string MaterialB)> SeedPartnersAsync()
    {
        var tag = Tag();
        var supplier = await Fx.Suppliers().CreateAsync(
            new SupplierSaveDto { SupplierCode = "S" + tag, SupplierName = $"点测供应商 {tag}" });
        var a = await Fx.Materials().CreateAsync(
            new MaterialSaveDto { MaterialCode = "M" + tag + "A", MaterialName = "钢板", Spec = "100×100", Unit = "pcs" });
        var b = await Fx.Materials().CreateAsync(
            new MaterialSaveDto { MaterialCode = "M" + tag + "B", MaterialName = "电缆", Spec = "3×4", Unit = "m" });
        return (supplier.Id, supplier.SupplierName, a.Id, b.Id);
    }

    private static OrderLineSaveDto Line(string materialId, decimal qty, decimal price, decimal tax = 13)
        => new() { MaterialId = materialId, Quantity = qty, UnitPrice = price, TaxRate = tax };

    [Fact]
    public async Task Purchase_Order_Computes_Line_And_Header_Totals_And_Snapshots()
    {
        var (supplierId, _, matA, matB) = await SeedPartnersAsync();
        var svc = Fx.PurchaseOrders();
        var created = await svc.CreateAsync(new PurchaseOrderSaveDto
        {
            SupplierId = supplierId, OrderDate = new DateTime(2026, 1, 5),
            Lines = [Line(matA, 3, 100.005m), Line(matB, 1.5m, 99.99m, 6)]
        }, 1, "admin", 2);

        Assert.StartsWith("PO", created.DocNo);
        Assert.Equal(4.5m, created.TotalQty);
        // 3×100.005=300.015→300.02；1.5×99.99=149.985→149.99；合计 450.01（AwayFromZero 到分）
        Assert.Equal(450.01m, created.TotalAmount);
        Assert.Equal(BizDocStatus.Draft, created.Status);

        // 列表不返回明细，行数由整页分组查询补：这里钉住它不是恒 0 的摆设
        var listed = (await svc.PageAsync(new ScmDocQuery { Keyword = created.DocNo, PageSize = 10 }, 1)).Rows
            .Single(r => r.Id == created.Id);
        Assert.Equal(2, listed.LineCount);
        Assert.Empty(listed.Lines);

        var detail = await svc.GetAsync(long.Parse(created.Id), 1);
        Assert.Equal(2, detail.Lines.Count);
        Assert.Equal("钢板", detail.Lines[0].MaterialName); // 物料名称按 id 取快照存进行
        Assert.Equal("100×100", detail.Lines[0].Spec);
        Assert.Equal(300.02m, detail.Lines[0].Amount);
        Assert.Equal(149.99m, detail.Lines[1].Amount);

        // 编辑=整单覆盖明细：换成 1 行后不能留下上一版的残行，合计要跟着重算
        await svc.UpdateAsync(long.Parse(created.Id), new PurchaseOrderSaveDto
        {
            SupplierId = supplierId, OrderDate = new DateTime(2026, 1, 5),
            Lines = [Line(matB, 10m, 7.55m)]
        }, 1);
        var edited = await svc.GetAsync(long.Parse(created.Id), 1);
        Assert.Single(edited.Lines);
        Assert.Equal("电缆", edited.Lines[0].MaterialName);
        Assert.Equal(75.50m, edited.TotalAmount);
        Assert.Equal(10m, edited.TotalQty);

        await svc.DeleteAsync(long.Parse(created.Id), 1);
    }

    [Fact]
    public async Task Purchase_Order_Rejects_Bad_Lines_And_Unknown_Partner()
    {
        var (supplierId, supplierName, matA, _) = await SeedPartnersAsync();
        var svc = Fx.PurchaseOrders();

        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            new PurchaseOrderSaveDto { SupplierId = supplierId, Lines = [] }, 1, "admin", 2));
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            new PurchaseOrderSaveDto { SupplierId = supplierId, Lines = [Line("999999999999", 1, 1)] }, 1, "admin", 2));
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            new PurchaseOrderSaveDto { SupplierId = supplierId, Lines = [Line(matA, 0, 10)] }, 1, "admin", 2));
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            new PurchaseOrderSaveDto { SupplierId = supplierId, Lines = [Line(matA, 1, 10, 130)] }, 1, "admin", 2));
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            new PurchaseOrderSaveDto { SupplierId = "999999999999", Lines = [Line(matA, 1, 10)] }, 1, "admin", 2));

        // 校验失败不能留下半张单（按本用例独有的供应商名查；用 Mine 会被同类其它用例的残留单干扰）
        var page = await svc.PageAsync(new ScmDocQuery { Keyword = supplierName, PageSize = 5 }, 1);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task Purchase_Order_Without_Binding_Submits_Straight_To_Approved_Then_Locks()
    {
        var (supplierId, supplierName, matA, _) = await SeedPartnersAsync();
        var svc = Fx.PurchaseOrders();
        var created = await svc.CreateAsync(new PurchaseOrderSaveDto
        {
            SupplierId = supplierId, Lines = [Line(matA, 2, 50)]
        }, 1, "admin", 2);

        // 未绑定审批流=直通（与报销/采购申请同一语义），InstanceId 保持空
        var submitted = await svc.SubmitAsync(long.Parse(created.Id), 1, "admin", new FlowSubmitDto());
        Assert.Equal(BizDocStatus.Approved, submitted.Status);
        Assert.Null(submitted.InstanceId);

        // 已通过=锁定，不能再改不能删
        await Assert.ThrowsAsync<BizException>(() => svc.UpdateAsync(long.Parse(created.Id),
            new PurchaseOrderSaveDto { SupplierId = supplierId, Lines = [Line(matA, 9, 9)] }, 1));
        await Assert.ThrowsAsync<BizException>(() => svc.DeleteAsync(long.Parse(created.Id), 1));
    }

    [Fact]
    public async Task Order_Detail_Respects_Data_Scope_Of_Caller()
    {
        var (supplierId, supplierName, matA, _) = await SeedPartnersAsync();
        var tag = "sc" + Tag();
        var owner = await ProbeUserAsync(DataScopeType.Self, 3, "o_" + tag);
        var other = await ProbeUserAsync(DataScopeType.Self, 4, "p_" + tag);
        var svc = Fx.PurchaseOrders();
        var created = await svc.CreateAsync(new PurchaseOrderSaveDto
        {
            SupplierId = supplierId, Lines = [Line(matA, 1, 10)]
        }, owner.Id, owner.UserName, owner.DeptId);
        try
        {
            // 归属人自己看得见
            Assert.Equal(created.Id, (await svc.GetAsync(long.Parse(created.Id), owner.Id)).Id);
            // 同公司另一个「仅本人」档的人按 id 直读必须被拒（列表挡住了，详情也得挡住）
            var denied = await Assert.ThrowsAsync<BizException>(
                () => svc.GetAsync(long.Parse(created.Id), other.Id));
            Assert.Contains("无权查看", denied.Message);
            Assert.Equal(0, (await svc.PageAsync(new ScmDocQuery { PageSize = 50 }, other.Id)).Total);
        }
        finally
        {
            await svc.DeleteAsync(long.Parse(created.Id), owner.Id);
            await Db.Deleteable<SysUser>().Where(u => u.UserName.StartsWith("o_" + tag) || u.UserName.StartsWith("p_" + tag))
                .ExecuteCommandAsync();
            await Db.Deleteable<SysRole>().Where(r => r.RoleCode.StartsWith("r_" + tag)).ExecuteCommandAsync();
        }
    }

    [Fact]
    public async Task Sales_Order_Requires_Customer_And_Existing_Warehouse()
    {
        var tag = Tag();
        var customer = await Fx.Customers().CreateAsync(
            new CustomerSaveDto { CustomerCode = "C" + tag, CustomerName = "点测客户" });
        var material = await Fx.Materials().CreateAsync(
            new MaterialSaveDto { MaterialCode = "M" + tag, MaterialName = "样件" });
        var svc = Fx.SalesOrders();

        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            new SalesOrderSaveDto { CustomerId = customer.Id, WarehouseId = "999999999999",
                Lines = [Line(material.Id, 1, 20)] }, 1, "admin", 2));

        var created = await svc.CreateAsync(new SalesOrderSaveDto
        {
            CustomerId = customer.Id, DeliveryDate = new DateTime(2026, 2, 1), Lines = [Line(material.Id, 4, 20)]
        }, 1, "admin", 2);
        Assert.StartsWith("SO", created.DocNo);
        Assert.Equal(80m, created.TotalAmount);
        Assert.Equal("点测客户", created.CustomerName);

        await svc.DeleteAsync(long.Parse(created.Id), 1);
    }

    [Fact]
    public async Task Price_Agreement_Is_Unique_Pair_And_Quote_Respects_Window_And_Status()
    {
        var (supplierId, supplierName, matA, _) = await SeedPartnersAsync();
        var svc = Fx.Prices();
        var today = DateTime.Today;

        var created = await svc.CreateAsync(new PriceAgreementSaveDto
        {
            SupplierId = supplierId, MaterialId = matA, UnitPrice = 88.5m, TaxRate = 13,
            BeginDate = today.AddDays(-1), EndDate = today.AddDays(30)
        });
        Assert.StartsWith("点测供应商", created.SupplierName);

        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            new PriceAgreementSaveDto { SupplierId = supplierId, MaterialId = matA, UnitPrice = 1 }));
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            new PriceAgreementSaveDto
            {
                SupplierId = supplierId, MaterialId = matA, UnitPrice = 1,
                BeginDate = today, EndDate = today.AddDays(-1)
            }));

        Assert.NotNull(await svc.QuoteAsync(long.Parse(supplierId), long.Parse(matA)));

        // 停用后不再带出价（下单不能自动套一份作废的协议）
        await svc.UpdateAsync(long.Parse(created.Id), new PriceAgreementSaveDto
        {
            SupplierId = supplierId, MaterialId = matA, UnitPrice = 88.5m, TaxRate = 13,
            BeginDate = today.AddDays(-1), EndDate = today.AddDays(30), Status = EnableStatus.Disabled,
            Version = created.Version
        });
        Assert.Null(await svc.QuoteAsync(long.Parse(supplierId), long.Parse(matA)));

        await svc.DeleteAsync(long.Parse(created.Id));
    }

    [Fact]
    public async Task Warehouse_Keeps_At_Most_One_Default()
    {
        var tag = Tag();
        var svc = Fx.Warehouses();
        var first = await svc.CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "W" + tag + "1", WarehouseName = "主仓", IsDefault = true });
        var second = await svc.CreateAsync(
            new WarehouseSaveDto { WarehouseCode = "W" + tag + "2", WarehouseName = "次仓", IsDefault = true });

        var page = await svc.PageAsync(new WarehouseQuery { Keyword = "W" + tag, PageSize = 20 });
        Assert.False(page.Rows.Single(r => r.Id == first.Id).IsDefault);
        Assert.True(page.Rows.Single(r => r.Id == second.Id).IsDefault);

        await svc.DeleteAsync(long.Parse(first.Id));
        await svc.DeleteAsync(long.Parse(second.Id));
    }

    /// <summary>建一个「仅本人」档的一次性探针账号（不动用户数据）。</summary>
    private async Task<SysUser> ProbeUserAsync(DataScopeType scope, long? deptId, string userName)
    {
        var role = new SysRole
        {
            Id = SnowflakeId.NextId(), RoleCode = "r_" + userName, RoleName = "订单探针角色",
            DataScope = scope, Status = EnableStatus.Enabled, Sort = 999
        };
        var user = new SysUser
        {
            Id = SnowflakeId.NextId(), UserName = userName, NickName = "订单探针", Password = "not-used",
            DeptId = deptId, Status = EnableStatus.Enabled, PwdUpdateTime = DateTime.Now
        };
        user.OwnerUserId = user.Id;
        await Db.Insertable(role).ExecuteCommandAsync();
        await Db.Insertable(user).ExecuteCommandAsync();
        await Db.Insertable(new SysUserRole { Id = SnowflakeId.NextId(), UserId = user.Id, RoleId = role.Id })
            .ExecuteCommandAsync();
        return user;
    }
}
