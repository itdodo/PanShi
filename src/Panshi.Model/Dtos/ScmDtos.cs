using System.ComponentModel.DataAnnotations;
using Panshi.Model.Enums;

namespace Panshi.Model.Dtos;

/// <summary>
/// 供应链订单出入参。采购/销售两套 DTO 形状接近但刻意不合并：
/// 合并就得靠可空字段区分对方（SupplierId 与 CustomerId 同时存在且都能空），校验与语义都会糊掉。
/// 金额一律后端算（Amount/TotalAmount 不接收前端值），避免四舍五入口径不一致。
/// </summary>
public class OrderLineDto
{
    public string Id { get; set; } = "";

    public string MaterialId { get; set; } = "";

    public string MaterialCode { get; set; } = "";

    public string MaterialName { get; set; } = "";

    public string? Spec { get; set; }

    public string? Unit { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal TaxRate { get; set; }

    public decimal Amount { get; set; }

    public string? Remark { get; set; }
}

public class OrderLineSaveDto
{
    [Required]
    public string MaterialId { get; set; } = "";

    [Range(typeof(decimal), "0.0001", "99999999999999")]
    public decimal Quantity { get; set; }

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal UnitPrice { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal TaxRate { get; set; }

    [StringLength(256)]
    public string? Remark { get; set; }
}

/// <summary>订单列表查询（采购/销售共用；日期按含头含尾处理，前端传本地时区 startOf/endOf day）。</summary>
public class ScmDocQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public BizDocStatus? Status { get; set; }

    public bool Mine { get; set; }

    public string? SupplierId { get; set; }

    public string? CustomerId { get; set; }

    public DateTime? Begin { get; set; }

    public DateTime? End { get; set; }
}

public class PurchaseOrderDto
{
    public string Id { get; set; } = "";

    public string DocNo { get; set; } = "";

    public string OwnerUserId { get; set; } = "";

    public string OwnerUserName { get; set; } = "";

    public string? SupplierId { get; set; }

    public string SupplierName { get; set; } = "";

    public DateTime OrderDate { get; set; }

    public DateTime? DeliveryDate { get; set; }

    public decimal TotalQty { get; set; }

    public decimal TotalAmount { get; set; }

    public string? SourceRequestId { get; set; }

    public string? SourceRequestNo { get; set; }

    public BizDocStatus Status { get; set; }

    public string? InstanceId { get; set; }

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }

    /// <summary>明细行数：列表页只回头表，行数单独按整页分组查一次（不是逐行查）</summary>
    public int LineCount { get; set; }

    public List<OrderLineDto> Lines { get; set; } = [];
}

public class PurchaseOrderSaveDto
{
    [Required]
    public string SupplierId { get; set; } = "";

    public DateTime OrderDate { get; set; } = DateTime.Now;

    public DateTime? DeliveryDate { get; set; }

    public string? SourceRequestId { get; set; }

    [StringLength(512)]
    public string? Remark { get; set; }

    [Required, MinLength(1)]
    public List<OrderLineSaveDto> Lines { get; set; } = [];

    /// <summary>乐观锁回显值（红线 #6）</summary>
    public int Version { get; set; }
}

public class SalesOrderDto
{
    public string Id { get; set; } = "";

    public string DocNo { get; set; } = "";

    public string OwnerUserId { get; set; } = "";

    public string OwnerUserName { get; set; } = "";

    public string? CustomerId { get; set; }

    public string CustomerName { get; set; } = "";

    public DateTime OrderDate { get; set; }

    public DateTime? DeliveryDate { get; set; }

    public decimal TotalQty { get; set; }

    public decimal TotalAmount { get; set; }

    public string? WarehouseId { get; set; }

    public string? WarehouseName { get; set; }

    public BizDocStatus Status { get; set; }

    public string? InstanceId { get; set; }

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }

    /// <summary>明细行数：列表页只回头表，行数单独按整页分组查一次（不是逐行查）</summary>
    public int LineCount { get; set; }

    public List<OrderLineDto> Lines { get; set; } = [];
}

public class SalesOrderSaveDto
{
    [Required]
    public string CustomerId { get; set; } = "";

    public DateTime OrderDate { get; set; } = DateTime.Now;

    public DateTime? DeliveryDate { get; set; }

    public string? WarehouseId { get; set; }

    [StringLength(512)]
    public string? Remark { get; set; }

    [Required, MinLength(1)]
    public List<OrderLineSaveDto> Lines { get; set; } = [];

    public int Version { get; set; }
}

public class ArrivalDto
{
    public string Id { get; set; } = "";

    public string OrderId { get; set; } = "";

    public string OrderNo { get; set; } = "";

    public string OrderLineId { get; set; } = "";

    public string SupplierId { get; set; } = "";

    public string SupplierName { get; set; } = "";

    public string MaterialId { get; set; } = "";

    public string MaterialCode { get; set; } = "";

    public string MaterialName { get; set; } = "";

    public string? Spec { get; set; }

    public string? Unit { get; set; }

    public decimal PlanQty { get; set; }

    public DateTime PlanDate { get; set; }

    /// <summary>已过账的采购入库量（按订单号 + 物料汇总，作废的不算）</summary>
    public decimal ReceivedQty { get; set; }

    /// <summary>未收量；超收时为负数</summary>
    public decimal OpenQty { get; set; }

    public ArrivalStatus Status { get; set; }

    /// <summary>未收满且计划日已过——逾期是推导出来的，不存状态位，免得改期/入库后要回头同步</summary>
    public bool Overdue { get; set; }

    public bool Rescheduled { get; set; }

    public string OwnerUserName { get; set; } = "";

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class ArrivalQuery : PagedQuery
{
    /// <summary>命中订单号 / 供应商 / 物料编码与名称</summary>
    public string? Keyword { get; set; }

    public string? SupplierId { get; set; }

    public ArrivalStatus? Status { get; set; }

    /// <summary>只看逾期未收满的</summary>
    public bool OverdueOnly { get; set; }

    /// <summary>计划日到这个日期为止（含当天）</summary>
    public DateTime? DueBefore { get; set; }

    public bool Mine { get; set; }
}

public class ArrivalRescheduleDto
{
    [Required]
    public DateTime PlanDate { get; set; }

    [StringLength(256)]
    public string? Remark { get; set; }

    /// <summary>乐观锁回显值（红线 #6）</summary>
    public int Version { get; set; }
}
