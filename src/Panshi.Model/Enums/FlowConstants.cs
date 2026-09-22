namespace Panshi.Model.Enums;

/// <summary>审批流字符串常量（DSL/任务模式/流转动作，落库存字符串便于 JSON 对齐）。</summary>
public static class FlowConstants
{
    /// <summary>节点类型</summary>
    public static class NodeType
    {
        public const string Start = "start";
        public const string Approval = "approval";
        public const string Condition = "condition";
        public const string Cc = "cc";
        public const string End = "end";
    }

    /// <summary>审批方式（mode）</summary>
    public static class NodeMode
    {
        /// <summary>或签：一人定局，其余作废</summary>
        public const string OrSign = "orSign";

        /// <summary>会签：全员同意才过，任一拒即拒</summary>
        public const string Countersign = "countersign";

        /// <summary>依次：逐人生成待办（后续 Waiting）</summary>
        public const string Sequential = "sequential";
    }

    /// <summary>审批人来源（approvers[].type）</summary>
    public static class ApproverType
    {
        public const string User = "user";
        public const string Role = "role";
        public const string Position = "position";
        public const string DeptLeader = "deptLeader";

        /// <summary>发起人自选</summary>
        public const string SubmitterChoice = "submitterChoice";
    }

    /// <summary>岗位审批人范围（approvers[].scope）</summary>
    public static class ApproverScope
    {
        public const string Company = "company";
        public const string SubmitterDept = "submitterDept";
    }

    /// <summary>流转记录动作（sys_flow_record.Action）</summary>
    public static class Action
    {
        public const string Submit = "submit";
        public const string Approve = "approve";
        public const string Reject = "reject";
        public const string Transfer = "transfer";
        public const string AddSign = "addsign";
        public const string Return = "return";
        public const string Withdraw = "withdraw";
        public const string Cc = "cc";
        public const string Auto = "auto";
        public const string Void = "void";
    }

    /// <summary>条件操作符（ConditionEvaluator 解释执行，未知操作符=不命中）</summary>
    public static class Op
    {
        public const string Lt = "lt";
        public const string Le = "le";
        public const string Gt = "gt";
        public const string Ge = "ge";
        public const string Eq = "eq";
        public const string Ne = "ne";
        public const string In = "in";
        public const string Contains = "contains";
    }
}
