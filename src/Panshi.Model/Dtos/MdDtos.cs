using System.ComponentModel.DataAnnotations;
using Panshi.Model.Enums;

namespace Panshi.Model.Dtos;

/// <summary>
/// 基础资料三张表的出入参。雪花 id 一律字符串（红线：前端 id 用 string），
/// 金额用 decimal?——「没定价」与「价格为 0」要能区分。
/// </summary>
public class MaterialDto
{
    public string Id { get; set; } = "";

    public string MaterialCode { get; set; } = "";

    public string MaterialName { get; set; } = "";

    public string? Category { get; set; }

    public string? Spec { get; set; }

    public string? Unit { get; set; }

    public decimal? PurchasePrice { get; set; }

    public decimal? SalePrice { get; set; }

    public EnableStatus Status { get; set; }

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class MaterialSaveDto
{
    [Required, StringLength(32, MinimumLength = 2)]
    public string MaterialCode { get; set; } = "";

    [Required, StringLength(128)]
    public string MaterialName { get; set; } = "";

    [StringLength(64)]
    public string? Category { get; set; }

    [StringLength(128)]
    public string? Spec { get; set; }

    [StringLength(16)]
    public string? Unit { get; set; }

    public decimal? PurchasePrice { get; set; }

    public decimal? SalePrice { get; set; }

    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    [StringLength(512)]
    public string? Remark { get; set; }

    /// <summary>乐观锁回显值（红线 #6）</summary>
    public int Version { get; set; }
}

public class MaterialQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public string? Category { get; set; }

    public EnableStatus? Status { get; set; }
}

public class SupplierDto
{
    public string Id { get; set; } = "";

    public string SupplierCode { get; set; } = "";

    public string SupplierName { get; set; } = "";

    public string? ShortName { get; set; }

    public string? TaxNo { get; set; }

    public string? Contact { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? BankName { get; set; }

    public string? BankAccount { get; set; }

    public string? Category { get; set; }

    public EnableStatus Status { get; set; }

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class SupplierSaveDto
{
    [Required, StringLength(32, MinimumLength = 2)]
    public string SupplierCode { get; set; } = "";

    [Required, StringLength(128)]
    public string SupplierName { get; set; } = "";

    [StringLength(64)]
    public string? ShortName { get; set; }

    [StringLength(32)]
    public string? TaxNo { get; set; }

    [StringLength(32)]
    public string? Contact { get; set; }

    [StringLength(32)]
    public string? Phone { get; set; }

    [StringLength(64, MinimumLength = 3)]
    [EmailAddress]
    public string? Email { get; set; }

    [StringLength(256)]
    public string? Address { get; set; }

    [StringLength(64)]
    public string? BankName { get; set; }

    [StringLength(64)]
    public string? BankAccount { get; set; }

    [StringLength(64)]
    public string? Category { get; set; }

    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    [StringLength(512)]
    public string? Remark { get; set; }

    public int Version { get; set; }
}

public class SupplierQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public string? Category { get; set; }

    public EnableStatus? Status { get; set; }
}

public class CustomerDto
{
    public string Id { get; set; } = "";

    public string CustomerCode { get; set; } = "";

    public string CustomerName { get; set; } = "";

    public string? ShortName { get; set; }

    public string? TaxNo { get; set; }

    public string? Contact { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Address { get; set; }

    public string? Level { get; set; }

    public EnableStatus Status { get; set; }

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class CustomerSaveDto
{
    [Required, StringLength(32, MinimumLength = 2)]
    public string CustomerCode { get; set; } = "";

    [Required, StringLength(128)]
    public string CustomerName { get; set; } = "";

    [StringLength(64)]
    public string? ShortName { get; set; }

    [StringLength(32)]
    public string? TaxNo { get; set; }

    [StringLength(32)]
    public string? Contact { get; set; }

    [StringLength(32)]
    public string? Phone { get; set; }

    [StringLength(64, MinimumLength = 3)]
    [EmailAddress]
    public string? Email { get; set; }

    [StringLength(256)]
    public string? Address { get; set; }

    [StringLength(32)]
    public string? Level { get; set; }

    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    [StringLength(512)]
    public string? Remark { get; set; }

    public int Version { get; set; }
}

public class CustomerQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public string? Level { get; set; }

    public EnableStatus? Status { get; set; }
}

public class WarehouseDto
{
    public string Id { get; set; } = "";

    public string WarehouseCode { get; set; } = "";

    public string WarehouseName { get; set; } = "";

    public string? Address { get; set; }

    public string? Contact { get; set; }

    public string? Phone { get; set; }

    public bool IsDefault { get; set; }

    public EnableStatus Status { get; set; }

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class WarehouseSaveDto
{
    [Required, StringLength(32, MinimumLength = 2)]
    public string WarehouseCode { get; set; } = "";

    [Required, StringLength(128)]
    public string WarehouseName { get; set; } = "";

    [StringLength(256)]
    public string? Address { get; set; }

    [StringLength(32)]
    public string? Contact { get; set; }

    [StringLength(32)]
    public string? Phone { get; set; }

    public bool IsDefault { get; set; }

    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    [StringLength(512)]
    public string? Remark { get; set; }

    public int Version { get; set; }
}

public class WarehouseQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public EnableStatus? Status { get; set; }
}

public class PriceAgreementDto
{
    public string Id { get; set; } = "";

    public string SupplierId { get; set; } = "";

    public string SupplierName { get; set; } = "";

    public string MaterialId { get; set; } = "";

    public string MaterialCode { get; set; } = "";

    public string MaterialName { get; set; } = "";

    public decimal UnitPrice { get; set; }

    public decimal TaxRate { get; set; }

    public DateTime? BeginDate { get; set; }

    public DateTime? EndDate { get; set; }

    public EnableStatus Status { get; set; }

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class PriceAgreementSaveDto
{
    [Required]
    public string SupplierId { get; set; } = "";

    [Required]
    public string MaterialId { get; set; } = "";

    [Range(typeof(decimal), "0", "99999999999999")]
    public decimal UnitPrice { get; set; }

    [Range(typeof(decimal), "0", "100")]
    public decimal TaxRate { get; set; }

    public DateTime? BeginDate { get; set; }

    public DateTime? EndDate { get; set; }

    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    [StringLength(512)]
    public string? Remark { get; set; }

    public int Version { get; set; }
}

public class PriceAgreementQuery : PagedQuery
{
    public string? SupplierId { get; set; }

    public string? Keyword { get; set; }

    public EnableStatus? Status { get; set; }
}
