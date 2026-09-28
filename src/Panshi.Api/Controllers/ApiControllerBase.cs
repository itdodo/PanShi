using Panshi.Api.Middleware;

namespace Panshi.Api.Controllers;

/// <summary>
/// 管理端控制器公共底座。只收「每个控制器都要原样写一行」的成员，刻意不做更多——
/// 把业务方法往基类塞会让控制器退化成胖基类，那比复制一行更糟。
/// </summary>
public abstract class ApiControllerBase : Microsoft.AspNetCore.Mvc.ControllerBase
{
    /// <summary>当前登录用户 id（取自 JWT 的 NameIdentifier 声明，见 AuthSetup 里 NameClaimType 的指向）。</summary>
    protected long Uid => HttpContext.CurrentUserId();
}
