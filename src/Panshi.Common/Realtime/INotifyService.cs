namespace Panshi.Common.Realtime;

/// <summary>
/// 通知抽象（Service 层只依赖接口，Api 层用 SignalR 实现）：
/// 站内信落库 + 实时推送双写（notice / force-logout 事件）。
/// </summary>
public interface INotifyService
{
    /// <summary>通用通知：落库 sys_message + SignalR notice 事件（bizType/bizId 供前端跳转）。</summary>
    Task NotifyUserAsync(long userId, string title, string? content, string msgType = "system",
        string? bizType = null, long? bizId = null);

    /// <summary>批量通知（审批任务生成/抄送）。</summary>
    Task NotifyUsersAsync(IReadOnlyList<long> userIds, string title, string? content, string msgType = "business",
        string? bizType = null, long? bizId = null);

    /// <summary>强制下线（互踢/强踢/停用）：被踢端提示后 2 秒自动回登录页。</summary>
    Task ForceLogoutAsync(long userId, string reason);
}
