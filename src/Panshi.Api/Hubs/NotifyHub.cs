using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Panshi.Api.Hubs;

/// <summary>实时通道（/hubs/notify）：notice（通知/待办/抄送/终态）、force-logout（互踢/强踢/停用）。</summary>
[Authorize]
public class NotifyHub : Hub
{
}
