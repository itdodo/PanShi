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
