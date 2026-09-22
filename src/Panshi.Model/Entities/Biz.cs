using Panshi.Model.Enums;
using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>报销单（业务样板：演示「业务接入审批流」标准模式）</summary>
[SugarTable("biz_expense")]
public class BizExpense : BaseEntity, IDataScope
{
    /// <summary>单号（BX+yyyyMMdd+序号）</summary>
    [SugarColumn(Length = 32)]
    public string DocNo { get; set; } = "";

    /// <summary>申请人（数据权限归属人）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? OwnerUserId { get; set; }

    [SugarColumn(Length = 64)]
    public string OwnerUserName { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? DeptId { get; set; }

    /// <summary>报销金额（审批条件分支变量 amount）</summary>
    [SugarColumn(ColumnDataType = "numeric(18,2)")]
    public decimal Amount { get; set; }

    /// <summary>类别（字典 biz_expense_category）</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Category { get; set; }

    [SugarColumn(IsNullable = true, Length = 1024)]
    public string? Reason { get; set; }

    /// <summary>发票附件 sys_file Id 列表 JSON</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "text")]
    public string? AttachmentIds { get; set; }

    public BizDocStatus Status { get; set; } = BizDocStatus.Draft;

    /// <summary>当前审批实例（重新提交=新实例）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? InstanceId { get; set; }
}

/// <summary>采购申请单（业务样板）</summary>
[SugarTable("biz_purchase_request")]
public class BizPurchaseRequest : BaseEntity, IDataScope
{
    /// <summary>单号（CG+yyyyMMdd+序号）</summary>
    [SugarColumn(Length = 32)]
    public string DocNo { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? OwnerUserId { get; set; }

    [SugarColumn(Length = 64)]
    public string OwnerUserName { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? DeptId { get; set; }

    [SugarColumn(Length = 256)]
    public string ItemName { get; set; } = "";

    public int Quantity { get; set; } = 1;

    /// <summary>预算金额（审批条件分支变量 amount）</summary>
    [SugarColumn(ColumnDataType = "numeric(18,2)")]
    public decimal Amount { get; set; }

    [SugarColumn(IsNullable = true, Length = 1024)]
    public string? Reason { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "text")]
    public string? AttachmentIds { get; set; }

    public BizDocStatus Status { get; set; } = BizDocStatus.Draft;

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? InstanceId { get; set; }
}
