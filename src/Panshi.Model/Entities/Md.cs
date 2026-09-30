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
