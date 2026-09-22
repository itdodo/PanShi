using System.Text.Json.Serialization;

namespace Panshi.Model.Dtos;

/// <summary>
/// FlowGraph 设计器 DSL（存 sys_flow_definition.NodeJson，解释执行禁动态编译）。
/// 示例：
/// { "nodes":[ {"code":"start","type":"start","next":"c1"},
///   {"code":"c1","type":"condition","branches":[{"name":"小额","priority":1,"next":"n_low",
///     "conditions":[{"variable":"amount","op":"lt","value":10000}]}],"defaultNext":"n_high"},
///   {"code":"n1","type":"approval","name":"主管审批","mode":"orSign","next":"end",
///     "approvers":[{"type":"user","userIds":["1"]},{"type":"deptLeader","deptId":0}]},
///   {"code":"cc1","type":"cc","next":"end","ccUserIds":["1"],"ccRoleCodes":["hr"]},
///   {"code":"end","type":"end"} ], "entry":"start" }
/// </summary>
public class FlowGraph
{
    [JsonPropertyName("nodes")]
    public List<FlowNodeDto> Nodes { get; set; } = [];

    /// <summary>入口节点 code（通常 "start"）</summary>
    [JsonPropertyName("entry")]
    public string Entry { get; set; } = "start";

    /// <summary>按 code 找节点（缺失=DSL 非法，提交时校验拦截）。</summary>
    [JsonIgnore]
    public FlowNodeDto? Start => Nodes.FirstOrDefault(n => n.Code == Entry);

    public FlowNodeDto? Find(string code) => Nodes.FirstOrDefault(n => n.Code == code);
}

/// <summary>流程节点</summary>
public class FlowNodeDto
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = "";

    /// <summary>start/approval/condition/cc/end</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>后继节点 code（approval/cc/start）</summary>
    [JsonPropertyName("next")]
    public string? Next { get; set; }

    /// <summary>审批方式 orSign/countersign/sequential</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>审批人规则（多规则取并集）</summary>
    [JsonPropertyName("approvers")]
    public List<FlowApproverDto>? Approvers { get; set; }

    /// <summary>条件分支（priority 升序求值首个真分支）</summary>
    [JsonPropertyName("branches")]
    public List<FlowBranchDto>? Branches { get; set; }

    /// <summary>全不命中时的默认走向（缺省=直接结束）</summary>
    [JsonPropertyName("defaultNext")]
    public string? DefaultNext { get; set; }

    /// <summary>抄送人员 Id 列表（string 序列化的雪花 Id）</summary>
    [JsonPropertyName("ccUserIds")]
    public List<string>? CcUserIds { get; set; }

    /// <summary>抄送角色编码列表</summary>
    [JsonPropertyName("ccRoleCodes")]
    public List<string>? CcRoleCodes { get; set; }
}

/// <summary>审批人规则</summary>
public class FlowApproverDto
{
    /// <summary>user/role/position/deptLeader/submitterChoice</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("userIds")]
    public List<string>? UserIds { get; set; }

    [JsonPropertyName("roleCodes")]
    public List<string>? RoleCodes { get; set; }

    [JsonPropertyName("positionCodes")]
    public List<string>? PositionCodes { get; set; }

    /// <summary>position 作用域：company / submitterDept</summary>
    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    /// <summary>deptLeader 指定部门（0 或缺省=按发起人部门的 LeaderUserId 解析）</summary>
    [JsonPropertyName("deptId")]
    public long? DeptId { get; set; }
}

/// <summary>条件分支</summary>
public class FlowBranchDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>优先级（升序求值，首个真分支胜出）</summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 100;

    [JsonPropertyName("next")]
    public string Next { get; set; } = "";

    /// <summary>分支条件（AND 组合）</summary>
    [JsonPropertyName("conditions")]
    public List<FlowConditionDto> Conditions { get; set; } = [];
}

/// <summary>单条条件（变量缺失/未知操作符=不命中等）</summary>
public class FlowConditionDto
{
    [JsonPropertyName("variable")]
    public string Variable { get; set; } = "";

    /// <summary>lt/le/gt/ge/eq/ne/in/contains</summary>
    [JsonPropertyName("op")]
    public string Op { get; set; } = "";

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}
