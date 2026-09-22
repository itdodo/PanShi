using System.ComponentModel.DataAnnotations;
using Panshi.Model.Enums;

namespace Panshi.Model.Dtos;

/// <summary>报销单</summary>
public class ExpenseDto
{
    public string Id { get; set; } = "";

    public string DocNo { get; set; } = "";

    public string OwnerUserId { get; set; } = "";

    public string OwnerUserName { get; set; } = "";

    public string? DeptId { get; set; }

    public decimal Amount { get; set; }

    public string? Category { get; set; }

    public string? Reason { get; set; }

    public List<string> AttachmentIds { get; set; } = [];

    public BizDocStatus Status { get; set; }

    public string? InstanceId { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class ExpenseSaveDto
{
    [Range(typeof(decimal), "0.01", "999999999")]
    public decimal Amount { get; set; }

    [StringLength(64)]
    public string? Category { get; set; }

    [StringLength(1024)]
    public string? Reason { get; set; }

    public List<string> AttachmentIds { get; set; } = [];

    public int Version { get; set; }
}

/// <summary>采购申请单</summary>
public class PurchaseDto
{
    public string Id { get; set; } = "";

    public string DocNo { get; set; } = "";

    public string OwnerUserId { get; set; } = "";

    public string OwnerUserName { get; set; } = "";

    public string? DeptId { get; set; }

    public string ItemName { get; set; } = "";

    public int Quantity { get; set; } = 1;

    public decimal Amount { get; set; }

    public string? Reason { get; set; }

    public List<string> AttachmentIds { get; set; } = [];

    public BizDocStatus Status { get; set; }

    public string? InstanceId { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class PurchaseSaveDto
{
    [Required, StringLength(256)]
    public string ItemName { get; set; } = "";

    [Range(1, 999999)]
    public int Quantity { get; set; } = 1;

    [Range(typeof(decimal), "0.01", "999999999")]
    public decimal Amount { get; set; }

    [StringLength(1024)]
    public string? Reason { get; set; }

    public List<string> AttachmentIds { get; set; } = [];

    public int Version { get; set; }
}

public class BizDocQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public BizDocStatus? Status { get; set; }

    /// <summary>仅我的单据</summary>
    public bool Mine { get; set; }
}
