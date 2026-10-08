using System.Security.Cryptography;
using Serilog.Context;

namespace Panshi.Api.Middleware;

/// <summary>
/// 请求关联 ID：一条请求从进门到落日志用同一个号，用户报障时能直接引用。
/// 放在管道最前面（在 Serilog 请求日志与异常中间件之前），否则那两处拿不到。
/// </summary>
public class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";
    public const string LogProperty = "CorrelationId";

    /// <summary>调用方（网关/前端重试）带了就沿用，没带或带了脏值就自己发号。</summary>
    public static string Resolve(string? incoming) =>
        IsValid(incoming) ? incoming! : New();

    private static bool IsValid(string? value) =>
        value is { Length: >= 8 and <= 64 } && value.All(IsIdChar);

    // ⚠️ 只接受 [A-Za-z0-9._-]：这个值会进日志、也会回显到响应头，
    // 原样收下任意输入等于开一条日志注入与响应头拆分的旁路。
    private static bool IsIdChar(char c) =>
        c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-';

    public static string New() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    public static string? Current(HttpContext ctx) => ctx.Items[ItemKey] as string;

    public async Task InvokeAsync(HttpContext ctx)
    {
        var id = Resolve(ctx.Request.Headers[HeaderName].FirstOrDefault());
        ctx.Items[ItemKey] = id;
        ctx.Response.Headers[HeaderName] = id;

        using (LogContext.PushProperty(LogProperty, id))
        {
            await next(ctx);
        }
    }
}
