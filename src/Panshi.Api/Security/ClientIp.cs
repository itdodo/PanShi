using System.Net;

namespace Panshi.Api.Security;

/// <summary>
/// 全站唯一的「调用方 IP」入口。
/// 刻意**不**自己解析 X-Forwarded-For——那是 ForwardedHeaders 中间件的职责，重写一遍只会重写错
/// （漏了多级 XFF、把端口带进 key、以及最致命的「无条件相信客户端自带的 XFF」）。
/// 这里只做三件事：把受信代理名单解析成 IPNetwork、给限流一个稳定的分区键、
/// 以及在配置自相矛盾时**在启动阶段就炸**，而不是等到被攻击时才发现信任链是假的。
/// </summary>
public static class ClientIp
{
    /// <summary>取不到来源时的分区键。⚠️ 它会把所有取不到来源的请求塌进同一个桶——所以必须能在日志里看出来。</summary>
    public const string Unknown = "unknown";

    public const string TrustedProxiesKey = "Security:TrustedProxies";

    /// <summary>
    /// ForwardedHeaders 已按受信名单改写 Connection.RemoteIpAddress 之后的最终来源。
    /// 名单外（含未开启 TrustForwardedHeaders 时）这里就是真实 TCP 对端，客户端自带的 XFF 一律不采信。
    /// </summary>
    public static string Of(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? Unknown;

    /// <summary>限流分区键：与 Of 同源，额外保证永不为空（空桶=全局共享，是最危险的静默失效）。</summary>
    public static string PartitionKey(HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress?.ToString() ?? Unknown;

    /// <summary>
    /// 解析 Security:TrustedProxies（IP 或 CIDR 混写都行）。裸 IP 自动补 /32 或 /128。
    /// 任何一条写不进去就抛——配置期炸一次，胜过运行期默默信了不该信的人。
    /// </summary>
    public static List<IPNetwork> ParseTrustedProxies(IEnumerable<string> entries)
    {
        var nets = new List<IPNetwork>();
        foreach (var raw in entries)
        {
            var entry = raw?.Trim();
            if (string.IsNullOrEmpty(entry)) continue;
            var text = entry.Contains('/') ? entry : entry + (entry.Contains(':') ? "/128" : "/32");
            if (!IPNetwork.TryParse(text, out var net))
                throw new InvalidOperationException(
                    $"{TrustedProxiesKey} 含无法解析的条目 \"{entry}\"（规范化后 \"{text}\"）。" +
                    "接受 IP（172.19.0.1）或 CIDR（172.16.0.0/12），逗号分隔。");
            nets.Add(net);
        }

        return nets;
    }

    /// <summary>读配置里的受信代理名单：JSON 数组或逗号分隔字符串都接受（与本项目其它配置项写法一致）。</summary>
    public static List<IPNetwork> ReadTrustedProxies(IConfiguration cfg)
    {
        var arr = cfg.GetSection(TrustedProxiesKey).Get<string[]>();
        if (arr is { Length: > 0 }) return ParseTrustedProxies(arr);

        var inline = cfg[TrustedProxiesKey];
        return string.IsNullOrWhiteSpace(inline) ? [] : ParseTrustedProxies(inline.Split(','));
    }
}
