using System.ComponentModel.DataAnnotations;
using Panshi.Model.Enums;

namespace Panshi.Model.Dtos;

/// <summary>
/// 库存三块（单据 / 台账 / 流水）的出入参。
/// 数量一律 decimal(18,4) 透传，不做前端取整——kg/m 这类单位本来就有小数。
/// </summary>
public class StockDocLineDto
{
    public string Id { get; set; } = "";

    public string MaterialId { get; set; } = "";

    public string MaterialCode { get; set; } = "";

    public string MaterialName { get; set; } = "";

    public string? Spec { get; set; }

    public string? Unit { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>盘点单专用：过账时账面数（前端展示差异用，非盘点类型为空）</summary>
    public decimal? BookQty { get; set; }

    public string? Remark { get; set; }
}

public class StockDocLineSaveDto
{
    [Required]
    public string MaterialId { get; set; } = "";

    /// <summary>数量。盘点填实盘数（可为 0），其余类型必须 > 0</summary>
    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal Quantity { get; set; }

    [StringLength(256)]
    public string? Remark { get; set; }
}

public class StockDocDto
{
    public string Id { get; set; } = "";

    public string DocNo { get; set; } = "";

    public StockDocKind Kind { get; set; }

    public string WarehouseId { get; set; } = "";

    public string WarehouseName { get; set; } = "";

    public string? TargetWarehouseId { get; set; }

    public string? TargetWarehouseName { get; set; }

    public DateTime BizDate { get; set; }

    public string? SourceOrderNo { get; set; }

    public decimal TotalQty { get; set; }

    public StockDocStatus Status { get; set; }

    public DateTime? PostedTime { get; set; }

    public string OwnerUserId { get; set; } = "";

    public string OwnerUserName { get; set; } = "";

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }

    public int LineCount { get; set; }

    public List<StockDocLineDto> Lines { get; set; } = [];
}

public class StockDocSaveDto
{
    public StockDocKind Kind { get; set; } = StockDocKind.PurchaseIn;

    [Required]
    public string WarehouseId { get; set; } = "";

    public string? TargetWarehouseId { get; set; }

    public DateTime BizDate { get; set; } = DateTime.Now;

    [StringLength(32)]
    public string? SourceOrderNo { get; set; }

    [StringLength(512)]
    public string? Remark { get; set; }

    [Required, MinLength(1)]
    public List<StockDocLineSaveDto> Lines { get; set; } = [];

    /// <summary>乐观锁回显值（红线 #6）</summary>
    public int Version { get; set; }
}

public class StockDocQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public StockDocKind? Kind { get; set; }

    public StockDocStatus? Status { get; set; }

    public string? WarehouseId { get; set; }

    public bool Mine { get; set; }

    public DateTime? Begin { get; set; }

    public DateTime? End { get; set; }
}

public class StockDto
{
    public string Id { get; set; } = "";

    public string WarehouseId { get; set; } = "";

    public string WarehouseName { get; set; } = "";

    public string MaterialId { get; set; } = "";

    public string MaterialCode { get; set; } = "";

    public string MaterialName { get; set; } = "";

    public string? Spec { get; set; }

    public string? Unit { get; set; }

    public decimal Quantity { get; set; }

    public DateTime? UpdateTime { get; set; }
}

public class StockQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public string? WarehouseId { get; set; }

    /// <summary>只看有货的（现存量 > 0）</summary>
    public bool OnlyPositive { get; set; }
}

public class LedgerDto
{
    public string Id { get; set; } = "";

    public string DocId { get; set; } = "";

    public string DocNo { get; set; } = "";

    public StockDocKind Kind { get; set; }

    public string WarehouseId { get; set; } = "";

    public string WarehouseName { get; set; } = "";

    public string MaterialId { get; set; } = "";

    public string MaterialCode { get; set; } = "";

    public string MaterialName { get; set; } = "";

    public string? Unit { get; set; }

    public decimal ChangeQty { get; set; }

    public decimal BeforeQty { get; set; }

    public decimal AfterQty { get; set; }

    public DateTime BizTime { get; set; }

    public string OperatorName { get; set; } = "";
}

public class LedgerQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public StockDocKind? Kind { get; set; }

    public string? WarehouseId { get; set; }

    public string? MaterialId { get; set; }

    public DateTime? Begin { get; set; }

    public DateTime? End { get; set; }
}

public class StockAlertDto
{
    public string WarehouseId { get; set; } = "";

    public string WarehouseName { get; set; } = "";

    public string MaterialId { get; set; } = "";

    public string MaterialCode { get; set; } = "";

    public string MaterialName { get; set; } = "";

    public string? Spec { get; set; }

    public string? Unit { get; set; }

    public decimal? MinStock { get; set; }

    public decimal? MaxStock { get; set; }

    /// <summary>现存量（没有台账行按 0 算，不是 null）</summary>
    public decimal Quantity { get; set; }

    public StockAlertLevel Level { get; set; }

    /// <summary>Short=还差多少到下限；Over=超出上限多少</summary>
    public decimal Gap { get; set; }
}

public class StockAlertQuery : PagedQuery
{
    /// <summary>命中物料编码/名称</summary>
    public string? Keyword { get; set; }

    public string? WarehouseId { get; set; }

    public StockAlertLevel? Level { get; set; }
}

public class StockSummaryDto
{
    public string WarehouseId { get; set; } = "";

    public string WarehouseName { get; set; } = "";

    public string MaterialId { get; set; } = "";

    public string MaterialCode { get; set; } = "";

    public string MaterialName { get; set; } = "";

    public string? Spec { get; set; }

    public string? Unit { get; set; }

    /// <summary>期初结存：区间开始之前所有流水的累计和</summary>
    public decimal Opening { get; set; }

    /// <summary>本期收入：区间内正向变动合计</summary>
    public decimal Inbound { get; set; }

    /// <summary>本期发出：区间内负向变动合计（正数表示发出了多少）</summary>
    public decimal Outbound { get; set; }

    /// <summary>期末结存 = 期初 + 收入 − 发出</summary>
    public decimal Closing { get; set; }

    /// <summary>本期流水笔数</summary>
    public int Entries { get; set; }
}

public class StockSummaryQuery : PagedQuery
{
    /// <summary>统计起始日（必填，按整天含头）。期初要按它回看历史流水</summary>
    public DateTime? Begin { get; set; }

    /// <summary>统计截止日（含当天，留空=到今天）</summary>
    public DateTime? End { get; set; }

    public string? WarehouseId { get; set; }

    /// <summary>命中物料编码/名称</summary>
    public string? Keyword { get; set; }
}

public class SupplierPerformanceDto
{
    public string SupplierId { get; set; } = "";

    public string SupplierName { get; set; } = "";

    /// <summary>区间内已批准的采购订单数</summary>
    public int Orders { get; set; }

    public decimal Amount { get; set; }

    /// <summary>至少有一张采购入库单回填了本订单号的订单数</summary>
    public int Delivered { get; set; }

    /// <summary>已到货且首次入库不早于计划交期的订单数（分母只算填了计划交期的已到货订单）</summary>
    public int OnTime { get; set; }

    /// <summary>参与准交率统计的订单数（已到货且有计划交期）</summary>
    public int OnTimeBase { get; set; }

    /// <summary>一张入库单都没关联上的订单数——不是「没收货」，也可能是入库时没回填单号</summary>
    public int Pending { get; set; }

    /// <summary>准交率 0~1；OnTimeBase=0 时为 null（没有计划交期就不该编出一个比率）</summary>
    public decimal? OnTimeRate { get; set; }

    /// <summary>平均交付天数：首次入库日 − 下单日，按已到货订单取均值</summary>
    public decimal AvgLeadDays { get; set; }
}

public class SupplierPerformanceQuery : PagedQuery
{
    /// <summary>按下单日期筛（含头含尾），留空=最近一年</summary>
    public DateTime? Begin { get; set; }

    public DateTime? End { get; set; }

    /// <summary>命中供应商名称</summary>
    public string? Keyword { get; set; }
}
