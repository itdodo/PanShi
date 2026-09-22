using Panshi.Model.Entities;

namespace Panshi.Service.Flow;

/// <summary>
/// 业务回调接口（按 BusinessTable 关联——⚠️ 蓝图红线：勿按流程编码，编号自增且绑定可换会失联）。
/// ⚠️ 注册用 AddScoped 多实现（红线 #7：TryAddScoped 同接口多实现只收第一个）。
/// </summary>
public interface IFlowBusinessHandler
{
    /// <summary>关联业务表名（如 biz_expense）</summary>
    string BusinessTable { get; }

    /// <summary>待办/实例标题摘要（快照进 sys_flow_instance.Summary）</summary>
    Task<string?> GetSummaryAsync(long businessId);

    /// <summary>终态回写（引擎事务内执行）：status 通过/拒绝/撤回/作废 → 业务单据状态</summary>
    Task OnFinishedAsync(SysFlowInstance instance);
}
