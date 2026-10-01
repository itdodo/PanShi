using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Service.Md;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 基础资料三张表的共同契约：编码手填且「软删过滤下唯一」、options 只回启用的、
/// 软删后同编码可重新占用（服务层的 Exists 检查与迁移 0004 的部分唯一索引必须同口径，
/// 两边不一致就会出现「界面说重复、库里其实没有」或反过来）。
/// </summary>
[Collection("pg")]
public class MdMasterDataTests(PgFixture fx) : PgTestBase(fx)
{
    private static string Code(string prefix) => prefix + Guid.NewGuid().ToString("N")[..16];

    private MaterialService Materials() => new(Fx.Repo<MdMaterial>());

    private SupplierService Suppliers() => new(Fx.Repo<MdSupplier>());

    private CustomerService Customers() => new(Fx.Repo<MdCustomer>());

    private static MaterialSaveDto MaterialDto(string code, string name = "回归物料") => new()
    {
        MaterialCode = code, MaterialName = name, Category = "raw", Spec = "100×100", Unit = "pcs",
        PurchasePrice = 12.5m
    };

    [Fact]
    public async Task Seeded_Demo_Data_Is_Present_And_Quote_Resolves()
    {
        // 迁移 0006 灌的演示数据：物料 12 / 供应商 6 / 客户 6 / 仓库 3（1 默认）/ 协议价 10。
        // 断言用「至少」而不是精确值——用户在自己库里加料不该把测试弄红。
        var materials = await Fx.Materials().PageAsync(new MaterialQuery { PageSize = 200 });
        var suppliers = await Fx.Suppliers().PageAsync(new SupplierQuery { PageSize = 200 });
        var customers = await Fx.Customers().PageAsync(new CustomerQuery { PageSize = 200 });
        // 只翻种子前缀的那几条：测试库是共享的，别处建的仓按 create_time 倒序会把演示数据挤出首页
        var warehouses = await Fx.Warehouses().PageAsync(new WarehouseQuery { Keyword = "WH-", PageSize = 50 });
        Assert.True(materials.Total >= 12);
        Assert.True(suppliers.Total >= 6);
        Assert.True(customers.Total >= 6);
        // 种子确实把 WH-ZC 设成默认仓，但「默认」是可被别的仓抢走的（ScmOrderTests 的
        // Warehouse_Keeps_At_Most_One_Default 就会抢，且不会归还）——所以这里断言服务真正
        // 保证的不变量「至多一个默认」，而不是「此刻恰好是种子的哪一个」，否则用例互相依赖执行顺序。
        Assert.Contains(warehouses.Rows, w => w.WarehouseCode == "WH-ZC");
        Assert.True(await Db.Queryable<MdWarehouse>().Where(w => w.IsDefault).CountAsync() <= 1);

        // 演示数据要能真的支撑下单：供应商×物料能查到协议价，且带出税率
        var supplier = suppliers.Rows.First(r => r.SupplierCode == "SP-001");
        var material = materials.Rows.First(r => r.MaterialCode == "RM-ST01");
        var quote = await Fx.Prices().QuoteAsync(long.Parse(supplier.Id), long.Parse(material.Id));
        Assert.NotNull(quote);
        Assert.Equal(4520.00m, quote.UnitPrice);
        Assert.Equal(13m, quote.TaxRate);

        // 启用的主数据都进得了下拉
        Assert.NotEmpty(await Fx.Materials().OptionsAsync());
        Assert.NotEmpty(await Fx.Warehouses().OptionsAsync());
    }

    [Fact]
    public async Task Material_Code_Is_Unique_And_Reusable_After_Soft_Delete()
    {
        var code = Code("M");
        var svc = Materials();
        var created = await svc.CreateAsync(MaterialDto(code));
        Assert.Equal(code, created.MaterialCode);

        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(MaterialDto(code)));

        // 软删 → 服务层查不到、库里也允许同编码再插（部分唯一索引 WHERE is_deleted = false）
        await svc.DeleteAsync(long.Parse(created.Id));
        Assert.DoesNotContain(await svc.OptionsAsync(), o => o.Value == created.Id);
        var again = await svc.CreateAsync(MaterialDto(code, "改名复用"));
        Assert.NotEqual(created.Id, again.Id);
        Assert.Equal("改名复用", again.MaterialName);

        await svc.DeleteAsync(long.Parse(again.Id));
    }

    [Fact]
    public async Task Material_Options_Only_Return_Enabled_And_Page_Filters_By_Category()
    {
        var svc = Materials();
        var on = await svc.CreateAsync(MaterialDto(Code("M")));
        var off = await svc.CreateAsync(new MaterialSaveDto
        {
            MaterialCode = Code("M"), MaterialName = "停用料", Category = "semi", Status = EnableStatus.Disabled
        });
        try
        {
            var options = await svc.OptionsAsync();
            Assert.Contains(options, o => o.Value == on.Id);
            Assert.DoesNotContain(options, o => o.Value == off.Id);
            Assert.Contains("100×100", options.First(o => o.Value == on.Id).Label);

            var raw = await svc.PageAsync(new MaterialQuery { Category = "raw", PageSize = 200 });
            Assert.All(raw.Rows, r => Assert.Equal("raw", r.Category));
            Assert.DoesNotContain(raw.Rows, r => r.Id == off.Id);

            var byKeyword = await svc.PageAsync(new MaterialQuery { Keyword = on.MaterialCode, PageSize = 50 });
            Assert.Contains(byKeyword.Rows, r => r.Id == on.Id);
        }
        finally
        {
            await svc.DeleteAsync(long.Parse(on.Id));
            await svc.DeleteAsync(long.Parse(off.Id));
        }
    }

    [Fact]
    public async Task Supplier_Code_Unique_TaxNo_Uppercased_And_Update_Bumps_Version()
    {
        var svc = Suppliers();
        var code = Code("S");
        var created = await svc.CreateAsync(new SupplierSaveDto
        {
            SupplierCode = code, SupplierName = "回归供应商", TaxNo = "91310000ma1fl1xy2b", Contact = "张三",
            Phone = "13800000000", Category = "material"
        });
        Assert.Equal("91310000MA1FL1XY2B", created.TaxNo);

        var dup = await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
            new SupplierSaveDto { SupplierCode = code, SupplierName = "撞码" }));
        Assert.Contains("供应商编码已存在", dup.Message);

        await svc.UpdateAsync(long.Parse(created.Id), new SupplierSaveDto
        {
            SupplierCode = code, SupplierName = "回归供应商", TaxNo = "91310000ma1fl1xy2b", Contact = "李四",
            Status = EnableStatus.Enabled, Version = created.Version
        });
        var page = await svc.PageAsync(new SupplierQuery { Keyword = code, PageSize = 50 });
        var row = page.Rows.Single(r => r.Id == created.Id);
        Assert.Equal("李四", row.Contact);
        Assert.True(row.Version > created.Version);

        await svc.DeleteAsync(long.Parse(created.Id));
        Assert.DoesNotContain(await svc.OptionsAsync(), o => o.Value == created.Id);
    }

    [Fact]
    public async Task Customer_Code_Unique_And_Level_Filter_Works()
    {
        var svc = Customers();
        var a = await svc.CreateAsync(new CustomerSaveDto
        {
            CustomerCode = Code("C"), CustomerName = "A 类客户", Level = "A", ShortName = "甲"
        });
        var b = await svc.CreateAsync(new CustomerSaveDto
        {
            CustomerCode = Code("C"), CustomerName = "B 类客户", Level = "B"
        });
        try
        {
            await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(
                new CustomerSaveDto { CustomerCode = a.CustomerCode, CustomerName = "撞码" }));

            var levelA = await svc.PageAsync(new CustomerQuery { Level = "A", PageSize = 200 });
            Assert.Contains(levelA.Rows, r => r.Id == a.Id);
            Assert.DoesNotContain(levelA.Rows, r => r.Id == b.Id);

            // 关键字要能按简称命中（销售录单时常记得简称而不是正式名称）
            var byShort = await svc.PageAsync(new CustomerQuery { Keyword = "甲", PageSize = 200 });
            Assert.Contains(byShort.Rows, r => r.Id == a.Id);
        }
        finally
        {
            await svc.DeleteAsync(long.Parse(a.Id));
            await svc.DeleteAsync(long.Parse(b.Id));
        }
    }
}
