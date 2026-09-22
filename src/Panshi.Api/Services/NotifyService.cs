using Microsoft.AspNetCore.SignalR;
using Panshi.Api.Hubs;
using Panshi.Common.Realtime;
using Panshi.Common.Runtime;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;

namespace Panshi.Api.Services;

/// <summary>INotifyService 实现（Api 层，Service 层只依赖抽象）：落库 sys_message + SignalR 推送双写。</summary>
public class NotifyService(IHubContext<NotifyHub> hub, IRepository<SysMessage> msgRepo) : INotifyService
{
    public async Task NotifyUserAsync(long userId, string title, string? content, string msgType = "system",
        string? bizType = null, long? bizId = null)
    {
        await msgRepo.InsertAsync(new SysMessage
        {
            ReceiverId = userId, Title = title, Content = content,
            MsgType = ParseType(msgType), BizType = bizType, BizId = bizId,
            SenderId = OperationUser.UserId, SenderName = OperationUser.UserName
        });
        await hub.Clients.User(userId.ToString()).SendAsync("notice",
            new { title, content, msgType, bizType, bizId = bizId?.ToString(), time = DateTime.Now });
    }

    public async Task NotifyUsersAsync(IReadOnlyList<long> userIds, string title, string? content,
        string msgType = "business", string? bizType = null, long? bizId = null)
    {
        foreach (var uid in userIds.Distinct())
            await NotifyUserAsync(uid, title, content, msgType, bizType, bizId);
    }

    public async Task ForceLogoutAsync(long userId, string reason)
    {
        // 被踢端提示后 2 秒自动回登录页（前端约定）
        await hub.Clients.User(userId.ToString()).SendAsync("force-logout", new { reason });
    }

    private static MessageType ParseType(string msgType) => msgType switch
    {
        "business" => MessageType.Business,
        "insite" => MessageType.InSite,
        _ => MessageType.System
    };
}
