using Panshi.Model.Enums;
using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>
/// 库存单据（出入库/调拨/盘点）。一张单一个方向：
/// 入库类只动 WarehouseId，出库类同理，调拨同时用 TargetWarehouseId 做双边，盘点的行数量是「实盘数」。
/// 只有过账（Posted）才碰库存，草稿态对台账无影响。
/// </summary>
[SugarTable("scm_stock_doc")]
public class ScmStockDoc : BaseEntity, IDataScope
{
    /// <summary>单号（前缀按类型 + yyyyMMdd + 3 位序号）</summary>
    [SugarColumn(Length = 32)]
    public string DocNo { get; set; } = "";

    public StockDocKind Kind { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long WarehouseId { get; set; }

    [SugarColumn(Length = 128)]
    public string WarehouseName { get; set; } = "";

    /// <summary>调拨目标仓；其余类型为空</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? TargetWarehouseId { get; set; }

    [SugarColumn(IsNullable = true, Length = 128)]
    public string? TargetWarehouseName { get; set; }

    /// <summary>业务发生日期（流水按它排序）</summary>
    public DateTime BizDate { get; set; }

    /// <summary>来源单号快照（采购入库回链订单号等，可空）</summary>
    [SugarColumn(IsNullable = true, Length = 32)]
    public string? SourceOrderNo { get; set; }

    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal TotalQty { get; set; }

    public StockDocStatus Status { get; set; } = StockDocStatus.Draft;

    /// <summary>过账时间（作废不动它，靠 Status 区分）</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? PostedTime { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? OwnerUserId { get; set; }

    [SugarColumn(Length = 64)]
    public string OwnerUserName { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? DeptId { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}

/// <summary>库存单据明细行。没有价格列——库存单据只管数量，钱在订单和协议价那边。</summary>
[SugarTable("scm_stock_doc_line")]
public class ScmStockDocLine : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long DocId { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long MaterialId { get; set; }

    [SugarColumn(Length = 32)]
    public string MaterialCode { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string MaterialName { get; set; } = "";

    [SugarColumn(IsNullable = true, Length = 128)]
    public string? Spec { get; set; }

    [SugarColumn(IsNullable = true, Length = 16)]
    public string? Unit { get; set; }

    /// <summary>数量。盘点单这里是「实盘数」，过账时才换算成差额</summary>
    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal Quantity { get; set; }

    [SugarColumn(IsNullable = true, Length = 256)]
    public string? Remark { get; set; }
}

/// <summary>
/// 库存台账：仓库 × 物料一行现存量。唯一索引见迁移 0007（软删过滤下）。
/// 只有过账能改它，所以任何时刻它都等于流水的累计和——这条不变量由 StockDocService 的过账事务保证。
/// </summary>
[SugarTable("scm_stock")]
public class ScmStock : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long WarehouseId { get; set; }

    [SugarColumn(Length = 128)]
    public string WarehouseName { get; set; } = "";

    [SugarColumn(ColumnDataType = "bigint")]
    public long MaterialId { get; set; }

    [SugarColumn(Length = 32)]
    public string MaterialCode { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string MaterialName { get; set; } = "";

    [SugarColumn(IsNullable = true, Length = 128)]
    public string? Spec { get; set; }

    [SugarColumn(IsNullable = true, Length = 16)]
    public string? Unit { get; set; }

    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal Quantity { get; set; }
}

/// <summary>
/// 库存流水（只读账本）。每过账一行记一条，带变动前/变动后结存，供追溯与对账。
/// 快照字段一律自带：单据或物料后续被改被删，流水也得说清当时动了什么。
/// </summary>
[SugarTable("scm_stock_ledger")]
public class ScmStockLedger : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long DocId { get; set; }

    [SugarColumn(Length = 32)]
    public string DocNo { get; set; } = "";

    public StockDocKind Kind { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long WarehouseId { get; set; }

    [SugarColumn(Length = 128)]
    public string WarehouseName { get; set; } = "";

    [SugarColumn(ColumnDataType = "bigint")]
    public long MaterialId { get; set; }

    [SugarColumn(Length = 32)]
    public string MaterialCode { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string MaterialName { get; set; } = "";

    [SugarColumn(IsNullable = true, Length = 16)]
    public string? Unit { get; set; }

    /// <summary>带符号的变动量（出为负）</summary>
    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal ChangeQty { get; set; }

    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal BeforeQty { get; set; }

    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal AfterQty { get; set; }

    public DateTime BizTime { get; set; }

    [SugarColumn(Length = 64)]
    public string OperatorName { get; set; } = "";
}
