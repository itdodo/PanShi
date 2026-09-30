using System.Text.Json;
using Panshi.Api.Security;
using Panshi.Common.Json;
using Panshi.Common.Results;
using Panshi.Model.Enums;
using Panshi.Service.Sys;

namespace Panshi.Api.Middleware;

/// <summary>
/// IP 黑白名单闸门（安全 P1）。必须挂在 UseForwardedHeaders 之后、UseRateLimiter 之前：
/// 之后才拿得到可信来源，之前拦下才不必为注定被拒的请求去做限流与鉴权。
/// 白名单命中会在 HttpContext.Items 上打标记，限流策略据此给 NoLimiter 分区——
/// 运维跳板机与监控探针不该被自己的防爆破规则挡住。
/// </summary>
public class IpGuardMiddleware(RequestDelegate next, ILogger<IpGuardMiddleware> logger)
{
    /// <summary>限流策略读这个键来决定是否豁免。</summary>
    public const string WhitelistedKey = "panshi:ipguard:whitelisted";

    /// <summary>
    /// 豁免路径。⚠️ 健康检查必须豁免：否则一次误封会让编排把容器判成不健康并反复重启，
    /// 把一个「挡住攻击者」的功能变成「挡住自己」。
    /// </summary>
    private static readonly string[] ExemptPaths = ["/api/v1/health"];

    public async Task InvokeAsync(HttpContext ctx, IpGuardService guard)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (ExemptPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(ctx);
            return;
        }

        var ip = ClientIp.Of(ctx);
        var d = await guard.EvaluateAsync(ip);
        if (!d.Matched)
        {
            await next(ctx);
            return;
        }

        if (d.Kind == IpRuleKind.White)
        {
            ctx.Items[WhitelistedKey] = true;
            logger.LogInformation("IP 白名单放行并豁免限流 rule={Rule} ip={Ip} path={Path}", d.Cidr, ip, path);
            await next(ctx);
            return;
        }

        if (!d.Allowed)
        {
            logger.LogWarning("IP 黑名单拒绝 rule={Rule} ip={Ip} path={Path} ua={Ua}", d.Cidr, ip, path,
                ctx.Request.Headers.UserAgent.ToString());
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(
                ApiResult.Fail(403, "来源被管理员列入黑名单"), JsonConfig.Options));
            return;
        }

        // DryRun：命中但不拦——先看清会不会误杀，再决定放行拦截
        logger.LogWarning("IP 黑名单命中（DryRun 未拦截）rule={Rule} ip={Ip} path={Path}", d.Cidr, ip, path);
        await next(ctx);
    }
}
