using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Panshi.Api.Authorization;
using Panshi.Api.Controllers;
using Panshi.Api.Middleware;
using Xunit;

namespace Panshi.Tests;

/// <summary>
/// 可观测性两件事的契约：请求关联 ID（复用/发号/清洗）与请求计数器（并发下不丢数）。
/// 外加一条端点权限契约——/monitor/server 与 /monitor/metrics 都会吐机器名、PG 版本、库大小，
/// 属于「内部情报」，不能只挂 [Authorize] 让任何登录用户可读。
/// </summary>
public class ObservabilityTests
{
    /* ---------------- 关联 ID ---------------- */

    [Fact]
    public void 没带请求头时发一个十六进制号且两次不同()
    {
        var a = CorrelationIdMiddleware.Resolve(null);
        var b = CorrelationIdMiddleware.Resolve("");
        Assert.Matches("^[0-9a-f]{16}$", a);
        Assert.Matches("^[0-9a-f]{16}$", b);
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("gateway-req-0001")]
    [InlineData("a.b_c-d12345678")]
    public void 合法的上游id原样沿用(string incoming)
        => Assert.Equal(incoming, CorrelationIdMiddleware.Resolve(incoming));

    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    public void 长度边界内的合法字符照常沿用(int len)
    {
        var id = new string('a', len);
        Assert.Equal(id, CorrelationIdMiddleware.Resolve(id));
    }

    [Theory]
    [InlineData(7)]
    [InlineData(65)]
    public void 长度越界一律重新发号(int len)
    {
        var id = new string('a', len);
        Assert.NotEqual(id, CorrelationIdMiddleware.Resolve(id));
    }

    [Theory]
    [InlineData("has space")] // 空格
    [InlineData("<script>alert(1)</script>")] // 标签
    [InlineData("中文标识符")] // 非白名单字符
    [InlineData("semi;colon")] // 日志注入常用分隔符
    [InlineData("a\r\nb")] // 换行：能伪造日志行，也能拆响应头
    public void 脏输入一律重新发号(string dirty)
    {
        var resolved = CorrelationIdMiddleware.Resolve(dirty);
        Assert.NotEqual(dirty, resolved);
        Assert.Matches("^[0-9a-f]{16}$", resolved);
    }

    /* ---------------- 请求计数器 ---------------- */

    [Fact]
    public void 并发进出后计数不丢且活跃归零()
    {
        var m = new RequestMetrics();
        var statuses = new[] { 200, 200, 400, 401, 403, 409, 429, 500 };
        Parallel.For(0, 8000, i =>
        {
            m.Enter();
            m.Leave(statuses[i % statuses.Length], i % 50);
        });

        var s = m.Take();
        Assert.Equal(8000, s.Total);
        Assert.Equal(0, s.Active); // 活跃数必须回落，否则说明有请求没被 finally 记上
        Assert.Equal(1000, s.ServerErrors); // 500 每 8 个一次
        Assert.Equal(1000, s.ClientErrors); // 400
        Assert.Equal(1000, s.Unauthorized);
        Assert.Equal(1000, s.Forbidden);
        Assert.Equal(1000, s.Conflict);
        Assert.Equal(1000, s.Throttled);
        Assert.Equal(49, s.MaxMs); // 状态码分布之外，耗时上界也要能取到
        Assert.InRange(s.AvgMs, 22, 24);
    }

    [Fact]
    public void 只进未出的请求算作活跃()
    {
        var m = new RequestMetrics();
        m.Enter();
        m.Enter();
        Assert.Equal(2, m.Take().Active);
        m.Leave(200, 1);
        Assert.Equal(1, m.Take().Active);
    }

    [Fact]
    public void 零请求时平均耗时是零而不是除零()
        => Assert.Equal(0, new RequestMetrics().Take().AvgMs);

    [Fact]
    public void 指标计数序列化成数字而不是字符串()
    {
        // 全局那条 long→string 是给雪花 id 的；指标要是变成 "7"，jq/Prometheus 侧就得先 tonumber
        var json = System.Text.Json.JsonSerializer.Serialize(
            new MetricsDto { Requests = new RequestCountersDto { Total = 7, Active = 1, MaxMs = 1196 } },
            Panshi.Common.Json.JsonConfig.Options);
        Assert.Contains("\"total\":7", json);
        Assert.Contains("\"active\":1", json);
        Assert.Contains("\"maxMs\":1196", json);
        Assert.DoesNotContain("\"7\"", json);
    }

    /* ---------------- 端点权限契约 ---------------- */

    private static string? MethodPolicy(Type controller, string action) =>
        controller.GetMethod(action)!.GetCustomAttribute<AuthorizeAttribute>(inherit: false)?.Policy;

    [Fact]
    public void 服务监控与指标端点都要权限码()
    {
        // 种子里早就有 monitor:server:list（服务监控菜单），此前端点却没挂它
        Assert.Equal(PermPolicyNames.For("monitor:server:list"),
            MethodPolicy(typeof(MonitorController), nameof(MonitorController.Server)));
        Assert.Equal(PermPolicyNames.For("monitor:server:list"),
            MethodPolicy(typeof(MonitorController), nameof(MonitorController.Metrics)));
    }
}
