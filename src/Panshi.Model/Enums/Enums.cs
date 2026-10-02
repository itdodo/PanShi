using SqlSugar;

namespace Panshi.Model.Enums;

/// <summary>启用状态（sys_user/sys_role/menu/position/dict_data/notice 通用）</summary>
public enum EnableStatus
{
    /// <summary>正常</summary>
    Enabled = 0,

    /// <summary>停用</summary>
    Disabled = 1
}

/// <summary>菜单类型</summary>
public enum MenuType
{
    /// <summary>目录</summary>
    Directory = 1,

    /// <summary>菜单（页面）</summary>
    Menu = 2,

    /// <summary>按钮（仅作权限码载体）</summary>
    Button = 3
}

/// <summary>数据权限范围（五档）</summary>
public enum DataScopeType
{
    /// <summary>全部数据</summary>
    All = 1,

    /// <summary>本部门</summary>
    Dept = 2,

    /// <summary>本部门及以下</summary>
    DeptAndChild = 3,

    /// <summary>仅本人</summary>
    Self = 4,

    /// <summary>自定义（sys_role_dept）</summary>
    Custom = 5
}

/// <summary>公告类型</summary>
public enum NoticeType
{
    /// <summary>通知</summary>
    Notification = 1,

    /// <summary>公告</summary>
    Announcement = 2
}

/// <summary>公告状态（定时发布由 sys.notice.publish 作业到期 2→1）</summary>
public enum NoticeStatus
{
    /// <summary>停用</summary>
    Stopped = 0,

    /// <summary>已发布</summary>
    Published = 1,

    /// <summary>定时发布</summary>
    Scheduled = 2
}

/// <summary>站内信类型</summary>
public enum MessageType
{
    /// <summary>系统</summary>
    System = 1,

    /// <summary>站内信</summary>
    InSite = 2,

    /// <summary>业务</summary>
    Business = 3
}

/// <summary>审批实例状态</summary>
public enum FlowInstanceStatus
{
    /// <summary>审批中</summary>
    Running = 1,

    /// <summary>通过</summary>
    Approved = 2,

    /// <summary>拒绝（终态）</summary>
    Rejected = 3,

    /// <summary>撤回（发起人无人处理时可撤）</summary>
    Withdrawn = 4,

    /// <summary>作废</summary>
    Voided = 5
}

/// <summary>审批任务状态</summary>
public enum FlowTaskStatus
{
    /// <summary>待办</summary>
    Pending = 1,

    /// <summary>同意</summary>
    Agreed = 2,

    /// <summary>拒绝</summary>
    Rejected = 3,

    /// <summary>转办（原任务止，新人生成待办）</summary>
    Transferred = 4,

    /// <summary>自动通过（审批人=发起人/已审重复/退回后不适用时为常规）</summary>
    AutoPassed = 5,

    /// <summary>失效（或签一人定局/拒绝终态/驳回时其余待办作废）</summary>
    Invalidated = 6,

    /// <summary>等待（依次审批中尚未轮到）</summary>
    Waiting = 7,

    /// <summary>已驳回（驳回至节点动作的落地状态）</summary>
    Returned = 8
}

/// <summary>业务单据审批状态（Biz* 样板）</summary>
public enum BizDocStatus
{
    /// <summary>草稿（未提交）</summary>
    Draft = 0,

    /// <summary>审批中</summary>
    Running = 1,

    /// <summary>通过</summary>
    Approved = 2,

    /// <summary>拒绝</summary>
    Rejected = 3,

    /// <summary>撤回</summary>
    Withdrawn = 4
}

/// <summary>IP 名单类型。白名单优先于黑名单（先命中先放行），且白名单同时豁免限流。</summary>
public enum IpRuleKind
{
    /// <summary>黑名单：命中即拒</summary>
    Black = 1,

    /// <summary>白名单：命中即放行，并豁免限流</summary>
    White = 2
}

/// <summary>IP 名单来源。自动项必须带过期时间，且永不自动产生永久规则。</summary>
public enum IpRuleSource
{
    /// <summary>人工配置</summary>
    Manual = 1,

    /// <summary>系统自动（如凭据滥用作动）</summary>
    Auto = 2
}

/// <summary>
/// 库存单据类型。方向决定过账符号：入库类 +、出库类 -、调拨一单双边、盘点按差额。
/// 编号前缀见 StockDocService.Prefixes。
/// </summary>
public enum StockDocKind
{
    /// <summary>采购入库（RK）</summary>
    PurchaseIn = 1,

    /// <summary>销售出库（CK）</summary>
    SalesOut = 2,

    /// <summary>其他入库（QRK）</summary>
    OtherIn = 3,

    /// <summary>其他出库（QCK）</summary>
    OtherOut = 4,

    /// <summary>调拨（DB，源仓出、目标仓入）</summary>
    Transfer = 5,

    /// <summary>盘点（PD，行填实盘数，过账按「实盘 − 账面」生成差额）</summary>
    Count = 6
}

/// <summary>
/// 库存单据状态。刻意不复用 BizDocStatus：出入库要即时，不挂审批流，
/// 「已过账」才是它唯一有副作用的状态（写台账 + 记流水）。
/// </summary>
public enum StockDocStatus
{
    /// <summary>草稿：可改可删，未影响库存</summary>
    Draft = 0,

    /// <summary>已过账：库存已变动，单据与明细一律锁定</summary>
    Posted = 1,

    /// <summary>已作废：过账后发现错，用反向单冲销后作废本单</summary>
    Void = 2
}

/// <summary>库存预警档位</summary>
public enum StockAlertLevel
{
    /// <summary>低于下限（含完全没有台账行的 0 存量）</summary>
    Short = 1,

    /// <summary>高于上限</summary>
    Over = 2
}

/// <summary>到货计划收货进度（由「计划量 vs 已过账采购入库量」实时推导，不落库）</summary>
public enum ArrivalStatus
{
    /// <summary>还没收到货</summary>
    Pending = 1,

    /// <summary>部分到货</summary>
    Partial = 2,

    /// <summary>收满（含超收）</summary>
    Done = 3
}
