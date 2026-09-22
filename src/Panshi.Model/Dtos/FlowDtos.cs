using System.ComponentModel.DataAnnotations;
using Panshi.Model.Enums;

namespace Panshi.Model.Dtos;

// ---------------- 流程定义 ----------------
public class FlowDefQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public string? Category { get; set; }

    public int? Status { get; set; }
}

public class FlowDefDto
{
    public string Id { get; set; } = "";

    public string FlowCode { get; set; } = "";

    public string FlowName { get; set; } = "";

    public string? Category { get; set; }

    public string NodeJson { get; set; } = "";

    public int FlowVersion { get; set; }

    public int Status { get; set; }

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class FlowDefSaveDto
{
    [Required, StringLength(128)]
    public string FlowName { get; set; } = "";

    [StringLength(64)]
    public string? Category { get; set; }

    /// <summary>FlowGraph DSL（保存前服务端结构校验）</summary>
    [Required]
    public string NodeJson { get; set; } = "";

    [StringLength(512)]
    public string? Remark { get; set; }

    public int Version { get; set; }
}

/// <summary>启停/新版本发布</summary>
public class FlowDefEnableDto
{
    /// <summary>1启用（同编码唯一启用）0停用</summary>
    public int Status { get; set; }
}

// ---------------- 绑定 ----------------
public class FlowBindingDto
{
    public string Id { get; set; } = "";

    public string BusinessTable { get; set; } = "";

    public string FlowCode { get; set; } = "";

    public int Status { get; set; }

    public string? Remark { get; set; }

    public string? FlowName { get; set; }

    public int Version { get; set; }
}

public class FlowBindingSaveDto
{
    [Required, StringLength(64)]
    public string BusinessTable { get; set; } = "";

    [Required, StringLength(32)]
    public string FlowCode { get; set; } = "";

    public int Status { get; set; } = 1;

    [StringLength(512)]
    public string? Remark { get; set; }

    public int Version { get; set; }
}

// ---------------- 实例/任务 ----------------
public class FlowSubmitDto
{
    [Required]
    public string BusinessTable { get; set; } = "";

    [Required]
    public long BusinessId { get; set; }

    /// <summary>条件变量（金额等，字符串数字兼容）</summary>
    public Dictionary<string, object> Variables { get; set; } = new();

    /// <summary>submitterChoice 节点的自选审批人</summary>
    public List<string> ChoiceUserIds { get; set; } = [];
}

public class FlowActDto
{
    /// <summary>approve/reject</summary>
    [Required]
    public string Action { get; set; } = "";

    [StringLength(1024)]
    public string? Comment { get; set; }
}

public class FlowReturnDto
{
    [Required]
    public string TargetNodeCode { get; set; } = "";

    [StringLength(1024)]
    public string? Comment { get; set; }
}

public class FlowTransferDto
{
    [Required]
    public long ToUserId { get; set; }

    [StringLength(1024)]
    public string? Comment { get; set; }
}

public class FlowAddSignDto
{
    /// <summary>true=后加签（本节点通过后进入） false=前加签（并入当前节点共同把关）</summary>
    public bool After { get; set; } = true;

    [Required]
    public List<long> UserIds { get; set; } = [];

    [StringLength(1024)]
    public string? Comment { get; set; }
}

public class FlowTaskDto
{
    public string Id { get; set; } = "";

    public string InstanceId { get; set; } = "";

    public string NodeCode { get; set; } = "";

    public string NodeName { get; set; } = "";

    public string NodeMode { get; set; } = "";

    public string ApproverUserId { get; set; } = "";

    public string ApproverName { get; set; } = "";

    public FlowTaskStatus Status { get; set; }

    public int Sequence { get; set; }

    public string? Comment { get; set; }

    public DateTime? HandledTime { get; set; }

    public DateTime CreateTime { get; set; }

    // 实例摘要
    public string FlowName { get; set; } = "";

    public string? Summary { get; set; }

    public string SubmitterName { get; set; } = "";

    public string BusinessTable { get; set; } = "";

    public string BusinessId { get; set; } = "";

    public FlowInstanceStatus InstanceStatus { get; set; }
}

public class FlowInstanceDto
{
    public string Id { get; set; } = "";

    public string FlowCode { get; set; } = "";

    public string FlowName { get; set; } = "";

    public string BusinessTable { get; set; } = "";

    public string BusinessId { get; set; } = "";

    public string? Summary { get; set; }

    public string? CurrentNodeCode { get; set; }

    public FlowInstanceStatus Status { get; set; }

    public string SubmitterId { get; set; } = "";

    public string SubmitterName { get; set; } = "";

    public DateTime CreateTime { get; set; }

    public DateTime? FinishedTime { get; set; }
}

/// <summary>实例详情（时间线数据源）</summary>
public class FlowInstanceDetailDto
{
    public FlowInstanceDto Instance { get; set; } = new();

    public List<FlowTaskDto> Tasks { get; set; } = [];

    public List<FlowRecordDto> Records { get; set; } = [];

    public string NodeJson { get; set; } = "";
}

public class FlowRecordDto
{
    public string Id { get; set; } = "";

    public string? NodeCode { get; set; }

    public string? NodeName { get; set; }

    public string Action { get; set; } = "";

    public string? OperatorName { get; set; }

    public string? Comment { get; set; }

    public DateTime CreateTime { get; set; }
}

public class FlowCcDto
{
    public string Id { get; set; } = "";

    public string InstanceId { get; set; } = "";

    public string? NodeCode { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreateTime { get; set; }

    public FlowInstanceDto? Instance { get; set; }
}

public class FlowInstanceQuery : PagedQuery
{
    public string? FlowCode { get; set; }

    public FlowInstanceStatus? Status { get; set; }

    public string? Keyword { get; set; }
}

public class FlowTaskQuery : PagedQuery
{
    public string? Keyword { get; set; }
}
