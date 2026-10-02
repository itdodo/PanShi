using Panshi.Model.Enums;
using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>
/// 物料主数据。与业务单据（biz_*）分表分前缀：单据引用的是这里的编码，
/// 生命周期互不牵动——单据可以作废，主数据只能停用。
/// 刻意不实现 IDataScope：主数据全公司共享，按部门切会让采购单选不到料号。
/// </summary>
[SugarTable("md_material")]
public class MdMaterial : BaseEntity
{
    /// <summary>物料编码（软删过滤下唯一，见迁移 0004）</summary>
    [SugarColumn(Length = 32)]
    public string MaterialCode { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string MaterialName { get; set; } = "";

    /// <summary>分类（字典 md_material_category）</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Category { get; set; }

    /// <summary>规格型号</summary>
    [SugarColumn(IsNullable = true, Length = 128)]
    public string? Spec { get; set; }

    /// <summary>基本计量单位（字典 md_unit）</summary>
    [SugarColumn(IsNullable = true, Length = 16)]
    public string? Unit { get; set; }

    /// <summary>参考采购价。可空——「没定价」和「价格为 0」不是一回事</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "numeric(18,2)")]
    public decimal? PurchasePrice { get; set; }

    /// <summary>参考销售价</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "numeric(18,2)")]
    public decimal? SalePrice { get; set; }

    /// <summary>预警下限（安全库存）。空=该物料不做下限预警，与「下限=0」不同</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "numeric(18,4)")]
    public decimal? MinStock { get; set; }

    /// <summary>预警上限（最高储备）。空=不做上限预警</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "numeric(18,4)")]
    public decimal? MaxStock { get; set; }

    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}

/// <summary>供应商主数据（采购单据的下拉来源）。</summary>
[SugarTable("md_supplier")]
public class MdSupplier : BaseEntity
{
    [SugarColumn(Length = 32)]
    public string SupplierCode { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string SupplierName { get; set; } = "";

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? ShortName { get; set; }

    /// <summary>统一社会信用代码</summary>
    [SugarColumn(IsNullable = true, Length = 32)]
    public string? TaxNo { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? Contact { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? Phone { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Email { get; set; }

    [SugarColumn(IsNullable = true, Length = 256)]
    public string? Address { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? BankName { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? BankAccount { get; set; }

    /// <summary>分类（字典 md_supplier_category）</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Category { get; set; }

    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}

/// <summary>客户主数据（销售/发货单据的下拉来源）。</summary>
[SugarTable("md_customer")]
public class MdCustomer : BaseEntity
{
    [SugarColumn(Length = 32)]
    public string CustomerCode { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string CustomerName { get; set; } = "";

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? ShortName { get; set; }

    /// <summary>统一社会信用代码</summary>
    [SugarColumn(IsNullable = true, Length = 32)]
    public string? TaxNo { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? Contact { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? Phone { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Email { get; set; }

    [SugarColumn(IsNullable = true, Length = 256)]
    public string? Address { get; set; }

    /// <summary>等级（字典 md_customer_level）</summary>
    [SugarColumn(IsNullable = true, Length = 32)]
    public string? Level { get; set; }

    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}

/// <summary>
/// 仓库主数据。出入库单要选仓库，所以它属于基础资料而非供应链单据；
/// 全库只允许一个 IsDefault（服务层互斥维护），单据新建时默认带出。
/// </summary>
[SugarTable("md_warehouse")]
public class MdWarehouse : BaseEntity
{
    [SugarColumn(Length = 32)]
    public string WarehouseCode { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string WarehouseName { get; set; } = "";

    [SugarColumn(IsNullable = true, Length = 256)]
    public string? Address { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? Contact { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? Phone { get; set; }

    /// <summary>默认仓（新建单据不带仓库参数时用它；全库至多一个）</summary>
    public bool IsDefault { get; set; }

    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}

/// <summary>
/// 采购协议价：一个供应商 × 一个物料只留一行现行价（软删过滤下唯一）。
/// 调价直接改这行，历史靠字段级变更日志（sys_change_log）回溯，不在业务表里堆版本行。
/// </summary>
[SugarTable("md_price_agreement")]
public class MdPriceAgreement : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long SupplierId { get; set; }

    /// <summary>供应商名称快照：主数据改名不该改写历史报价的含义</summary>
    [SugarColumn(Length = 128)]
    public string SupplierName { get; set; } = "";

    [SugarColumn(ColumnDataType = "bigint")]
    public long MaterialId { get; set; }

    [SugarColumn(Length = 32)]
    public string MaterialCode { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string MaterialName { get; set; } = "";

    /// <summary>含税单价（下单选料时按此带出价格）</summary>
    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal UnitPrice { get; set; }

    /// <summary>税率百分比，如 13 表示 13%</summary>
    [SugarColumn(ColumnDataType = "numeric(5,2)")]
    public decimal TaxRate { get; set; }

    /// <summary>生效起始日；null = 立即生效</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? BeginDate { get; set; }

    /// <summary>失效日；null = 长期有效</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? EndDate { get; set; }

    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}
