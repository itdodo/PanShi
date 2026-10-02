using Panshi.Model.Enums;
using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>
/// 采购订单。明细单独建表（不用 JSON 塞进行里）——P1 的入库核销、执行进度、
/// 进销存报表都要按行查，JSON 会把这三条路全堵死。
/// 供应商/物料在这里存的是**名称快照**：主数据改名不能改写历史单据的含义。
/// </summary>
[SugarTable("scm_purchase_order")]
public class ScmPurchaseOrder : BaseEntity, IDataScope
{
    /// <summary>单号（PO+yyyyMMdd+3 位序号，后端生成，编辑不可改）</summary>
    [SugarColumn(Length = 32)]
    public string DocNo { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? OwnerUserId { get; set; }

    [SugarColumn(Length = 64)]
    public string OwnerUserName { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? DeptId { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long SupplierId { get; set; }

    [SugarColumn(Length = 128)]
    public string SupplierName { get; set; } = "";

    public DateTime OrderDate { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? DeliveryDate { get; set; }

    /// <summary>明细数量合计（后端按行汇总，前端只读）</summary>
    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal TotalQty { get; set; }

    /// <summary>含税金额合计</summary>
    [SugarColumn(ColumnDataType = "numeric(18,2)")]
    public decimal TotalAmount { get; set; }

    /// <summary>来源采购申请单（可空，存快照号便于列表直接显示）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? SourceRequestId { get; set; }

    [SugarColumn(IsNullable = true, Length = 32)]
    public string? SourceRequestNo { get; set; }

    public BizDocStatus Status { get; set; } = BizDocStatus.Draft;

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? InstanceId { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}

/// <summary>采购订单明细行。</summary>
[SugarTable("scm_purchase_order_line")]
public class ScmPurchaseOrderLine : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long OrderId { get; set; }

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

    /// <summary>含税单价</summary>
    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal UnitPrice { get; set; }

    /// <summary>税率百分比（13 = 13%）</summary>
    [SugarColumn(ColumnDataType = "numeric(5,2)")]
    public decimal TaxRate { get; set; }

    /// <summary>含税金额 = 数量 × 单价，后端算</summary>
    [SugarColumn(ColumnDataType = "numeric(18,2)")]
    public decimal Amount { get; set; }

    [SugarColumn(IsNullable = true, Length = 256)]
    public string? Remark { get; set; }
}

/// <summary>销售订单（与采购订单同构，分表是为了各自绑不同的审批流——见 sys_flow_binding）。</summary>
[SugarTable("scm_sales_order")]
public class ScmSalesOrder : BaseEntity, IDataScope
{
    [SugarColumn(Length = 32)]
    public string DocNo { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? OwnerUserId { get; set; }

    [SugarColumn(Length = 64)]
    public string OwnerUserName { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? DeptId { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long CustomerId { get; set; }

    [SugarColumn(Length = 128)]
    public string CustomerName { get; set; } = "";

    public DateTime OrderDate { get; set; }

    [SugarColumn(IsNullable = true)]
    public DateTime? DeliveryDate { get; set; }

    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal TotalQty { get; set; }

    [SugarColumn(ColumnDataType = "numeric(18,2)")]
    public decimal TotalAmount { get; set; }

    /// <summary>发货仓库（P0 只做记录，P1 出库单据此过账）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? WarehouseId { get; set; }

    /// <summary>仓库名称快照（与供应商/物料同理：改名不影响历史单据的可读性）</summary>
    [SugarColumn(IsNullable = true, Length = 128)]
    public string? WarehouseName { get; set; }

    public BizDocStatus Status { get; set; } = BizDocStatus.Draft;

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? InstanceId { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}

/// <summary>销售订单明细行。</summary>
[SugarTable("scm_sales_order_line")]
public class ScmSalesOrderLine : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long OrderId { get; set; }

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

    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal UnitPrice { get; set; }

    [SugarColumn(ColumnDataType = "numeric(5,2)")]
    public decimal TaxRate { get; set; }

    [SugarColumn(ColumnDataType = "numeric(18,2)")]
    public decimal Amount { get; set; }

    [SugarColumn(IsNullable = true, Length = 256)]
    public string? Remark { get; set; }
}

/// <summary>
/// 采购到货计划：一条已批准的订单行对应一条（唯一索引见迁移 0011）。
/// 只存「计划」——实际到货量在查询时从已过账的采购入库单实时算，
/// 所以过账路径不用改动，作废/红冲也会自动反映到未收量上。
/// </summary>
[SugarTable("scm_purchase_arrival")]
public class ScmPurchaseArrival : BaseEntity, IDataScope
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long OrderId { get; set; }

    /// <summary>采购订单号快照（对账时按它回链）</summary>
    [SugarColumn(Length = 32)]
    public string OrderNo { get; set; } = "";

    /// <summary>来源订单行；一行一条计划</summary>
    [SugarColumn(ColumnDataType = "bigint")]
    public long OrderLineId { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long SupplierId { get; set; }

    [SugarColumn(Length = 128)]
    public string SupplierName { get; set; } = "";

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

    /// <summary>计划到货数量，默认取订单行数量</summary>
    [SugarColumn(ColumnDataType = "numeric(18,4)")]
    public decimal PlanQty { get; set; }

    /// <summary>计划到货日，默认取订单交期；可在页面上逐行改期</summary>
    public DateTime PlanDate { get; set; }

    /// <summary>是否人工改过期。重新生成只补新行，绝不覆盖改过的计划</summary>
    public bool Rescheduled { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? OwnerUserId { get; set; }

    [SugarColumn(Length = 64)]
    public string OwnerUserName { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? DeptId { get; set; }

    [SugarColumn(IsNullable = true, Length = 256)]
    public string? Remark { get; set; }
}
