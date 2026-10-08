using System.Diagnostics;

namespace Panshi.Api.Middleware;

/// <summary>
/// 进程内请求计数器（单例）。只给**累计量**，不给百分位——
/// 想要 p95 就按固定间隔抓两次 <c>/api/v1/monitor/metrics</c> 自行算，
/// 或者接真监控；这里刻意不引 Prometheus 依赖（蓝图「明确不引入」清单）。
/// </summary>
public sealed class RequestMetrics
{
    private long _total;
    private long _active;
    private long _serverErrors;
    private long _clientErrors;
    private long _unauthorized;
    private long _forbidden;
    private long _conflict;
    private long _throttled;
    private long _durationMsSum;
    private long _durationMsMax;

    public void Enter()
    {
        Interlocked.Increment(ref _total);
        Interlocked.Increment(ref _active);
    }

    public void Leave(int statusCode, long elapsedMs)
    {
        Interlocked.Decrement(ref _active);
        Interlocked.Add(ref _durationMsSum, elapsedMs);
        var current = Volatile.Read(ref _durationMsMax);
        while (elapsedMs > current &&
               Interlocked.CompareExchange(ref _durationMsMax, elapsedMs, current) != current)
        {
            current = Volatile.Read(ref _durationMsMax);
        }

        switch (statusCode)
        {
            case 401: Interlocked.Increment(ref _unauthorized); break;
            case 403: Interlocked.Increment(ref _forbidden); break;
            case 409: Interlocked.Increment(ref _conflict); break;
            case StatusCodes.Status429TooManyRequests: Interlocked.Increment(ref _throttled); break;
            case >= 500: Interlocked.Increment(ref _serverErrors); break;
            case >= 400: Interlocked.Increment(ref _clientErrors); break;
        }
    }

    public sealed record Snapshot(
        long Total, long Active, long ServerErrors, long ClientErrors, long Unauthorized,
        long Forbidden, long Conflict, long Throttled, long AvgMs, long MaxMs);

    public Snapshot Take()
    {
        var total = Volatile.Read(ref _total);
        return new Snapshot(
            total, Volatile.Read(ref _active), Volatile.Read(ref _serverErrors),
            Volatile.Read(ref _clientErrors), Volatile.Read(ref _unauthorized),
            Volatile.Read(ref _forbidden), Volatile.Read(ref _conflict), Volatile.Read(ref _throttled),
            total == 0 ? 0 : Volatile.Read(ref _durationMsSum) / total, Volatile.Read(ref _durationMsMax));
    }
}

/// <summary>
/// 计数中间件：排在关联 ID 之后、异常中间件之前，这样连「异常直写响应」的请求也被算进状态码分布。
/// 只统计不拦截——限流另有 <c>UseRateLimiter</c>，别把这里当闸门用。
/// </summary>
public class RequestMetricsMiddleware(RequestDelegate next, RequestMetrics metrics)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var start = Stopwatch.GetTimestamp();
        metrics.Enter();
        try
        {
            await next(ctx);
        }
        finally
        {
            var ms = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            metrics.Leave(ctx.Response.StatusCode, ms);
        }
    }
}
