using Panshi.Model.Enums;
using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>审批流程定义（同编码多版本，唯一启用）</summary>
[SugarTable("sys_flow_definition")]
public class SysFlowDefinition : BaseEntity
{
    /// <summary>流程编码（系统自增数字串，100 起；同编码唯一启用由服务层保证）</summary>
    [SugarColumn(Length = 32)]
    public string FlowCode { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string FlowName { get; set; } = "";

    /// <summary>分类（如 财务/行政/采购）</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? Category { get; set; }

    /// <summary>FlowGraph 设计器 DSL JSON</summary>
    [SugarColumn(ColumnDataType = "text")]
    public string NodeJson { get; set; } = "{\"nodes\":[],\"entry\":\"start\"}";

    /// <summary>版本号（同 FlowCode 递增）</summary>
    public int FlowVersion { get; set; } = 1;

    /// <summary>0停用 1启用（同编码唯一启用）</summary>
    public int Status { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}

/// <summary>审批实例（单据运行时绑定：BusinessTable+BusinessId）</summary>
[SugarTable("sys_flow_instance")]
public class SysFlowInstance : BaseEntity
{
    [SugarColumn(Length = 32)]
    public string FlowCode { get; set; } = "";

    [SugarColumn(ColumnDataType = "bigint")]
    public long DefinitionId { get; set; }

    /// <summary>业务表名（与 sys_flow_binding / IFlowBusinessHandler 关联键）</summary>
    [SugarColumn(Length = 64)]
    public string BusinessTable { get; set; } = "";

    [SugarColumn(ColumnDataType = "bigint")]
    public long BusinessId { get; set; }

    /// <summary>待办标题摘要（GetSummaryAsync 生成）</summary>
    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Summary { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? CurrentNodeCode { get; set; }

    public FlowInstanceStatus Status { get; set; } = FlowInstanceStatus.Running;

    /// <summary>条件变量 JSON（提交时快照）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "text")]
    public string? VariablesJson { get; set; }

    /// <summary>后加签队列 JSON（[{nodeCode,name,approvers[]}]，本节点通过后先进追加节点）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "text")]
    public string? AppendNodesJson { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long SubmitterId { get; set; }

    [SugarColumn(Length = 64)]
    public string SubmitterName { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? SubmitterDeptId { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "timestamp")]
    public DateTime? FinishedTime { get; set; }
}

/// <summary>审批任务（人↔节点；含会签/或签/依次全部语义）</summary>
[SugarTable("sys_flow_task")]
public class SysFlowTask : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long InstanceId { get; set; }

    [SugarColumn(Length = 64)]
    public string NodeCode { get; set; } = "";

    [SugarColumn(Length = 128)]
    public string NodeName { get; set; } = "";

    /// <summary>本任务生成时所在节点的实际审批方式（orSign/countersign/sequential）</summary>
    [SugarColumn(Length = 32)]
    public string NodeMode { get; set; } = FlowConstants.NodeMode.OrSign;

    [SugarColumn(ColumnDataType = "bigint")]
    public long ApproverUserId { get; set; }

    [SugarColumn(Length = 64)]
    public string ApproverName { get; set; } = "";

    public FlowTaskStatus Status { get; set; } = FlowTaskStatus.Pending;

    /// <summary>依次审批顺序号（其余模式恒 0）</summary>
    public int Sequence { get; set; }

    [SugarColumn(IsNullable = true, Length = 1024)]
    public string? Comment { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "timestamp")]
    public DateTime? HandledTime { get; set; }
}

/// <summary>流转记录（时间线数据源，只追加不修改）</summary>
[SugarTable("sys_flow_record")]
public class SysFlowRecord : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long InstanceId { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? NodeCode { get; set; }

    [SugarColumn(IsNullable = true, Length = 128)]
    public string? NodeName { get; set; }

    /// <summary>submit/approve/reject/transfer/addsign/return/withdraw/cc/auto/void</summary>
    [SugarColumn(Length = 32)]
    public string Action { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? OperatorId { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? OperatorName { get; set; }

    [SugarColumn(IsNullable = true, Length = 1024)]
    public string? Comment { get; set; }

    /// <summary>扩展 JSON（目标节点/追加人/自动通过原因）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "text")]
    public string? ExtraJson { get; set; }
}

/// <summary>单据↔流程绑定（BusinessTable 唯一；空=不走审批；运行时可换绑/停用不发版）</summary>
[SugarTable("sys_flow_binding")]
public class SysFlowBinding : BaseEntity
{
    [SugarColumn(Length = 64)]
    public string BusinessTable { get; set; } = "";

    /// <summary>绑定的流程编码（迁移脚本建过滤唯一索引 business_table where is_deleted=false）</summary>
    [SugarColumn(Length = 32)]
    public string FlowCode { get; set; } = "";

    /// <summary>0停用 1启用</summary>
    public int Status { get; set; } = 1;

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}

/// <summary>抄送收件箱（到达即按人写入；「抄送我的」数据源）</summary>
[SugarTable("sys_flow_cc")]
public class SysFlowCc : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long InstanceId { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? NodeCode { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long UserId { get; set; }

    [SugarColumn(Length = 64)]
    public string UserName { get; set; } = "";

    public bool IsRead { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "timestamp")]
    public DateTime? ReadTime { get; set; }
}
