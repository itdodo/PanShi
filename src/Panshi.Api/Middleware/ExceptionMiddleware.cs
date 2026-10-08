using System.Text.Json;
using Panshi.Common.Exceptions;
using Panshi.Common.Json;
using Panshi.Common.Results;
using Serilog;

namespace Panshi.Api.Middleware;

/// <summary>
/// 全局异常映射：BizException.Code（4xx/409 语义）同时作为 HTTP 状态与业务码；
/// 其他异常 500（非开发环境不回显堆栈）。响应恒为统一 ApiResult 形状。
/// </summary>
public class ExceptionMiddleware(RequestDelegate next, IHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (BizException ex)
        {
            await WriteAsync(ctx, ex.Code is >= 400 and <= 599 ? ex.Code : StatusCodes.Status400BadRequest,
                ApiResult.Fail(ex.Code, ex.Message));
        }
        catch (Exception ex)
        {
            // 记关联 ID 而不是 TraceIdentifier：前者也回显在 X-Correlation-Id 响应头里，
            // 用户报障时能直接引用；TraceIdentifier 客户端看不到。
            var correlation = CorrelationIdMiddleware.Current(ctx) ?? ctx.TraceIdentifier;
            Log.Error(ex, "未处理异常 {Path} {CorrelationId}", ctx.Request.Path, correlation);
            await WriteAsync(ctx, StatusCodes.Status500InternalServerError,
                ApiResult.Fail(500, env.IsDevelopment() ? $"{ex.GetType().Name}: {ex.Message}" : "服务器内部错误，请联系管理员"));
        }
    }

    private static async Task WriteAsync(HttpContext ctx, int status, ApiResult body)
    {
        if (ctx.Response.HasStarted) return;
        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        await ctx.Response.WriteAsync(JsonSerializer.Serialize(body, JsonConfig.Options));
    }
}
