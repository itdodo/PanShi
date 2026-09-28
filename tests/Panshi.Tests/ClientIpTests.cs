using System.Net;
using Microsoft.AspNetCore.Http;
using Panshi.Api.Security;
using Xunit;

namespace Panshi.Tests;

/// <summary>
/// 来源 IP 解析（安全 P0）。这一层是将来黑名单与限流的信任根，
/// 所以最要紧的是「配错了要炸」而不是「配错了静默放宽」。
/// </summary>
public class ClientIpTests
{
    [Fact]
    public void Bare_Ipv4_Normalizes_To_Slash32_And_Matches_Only_Itself()
    {
        var nets = ClientIp.ParseTrustedProxies(["172.19.0.1"]);
        var net = Assert.Single(nets);
        Assert.Equal(32, net.PrefixLength);
        Assert.True(net.Contains(IPAddress.Parse("172.19.0.1")));
        Assert.False(net.Contains(IPAddress.Parse("172.19.0.2")));
    }

    [Fact]
    public void Cidr_Matches_Whole_Range()
    {
        var net = Assert.Single(ClientIp.ParseTrustedProxies(["172.16.0.0/12"]));
        Assert.True(net.Contains(IPAddress.Parse("172.20.1.1")));
        Assert.False(net.Contains(IPAddress.Parse("172.32.0.1"))); // /12 覆盖 172.16–172.31
    }

    [Fact]
    public void Bare_Ipv6_Normalizes_To_Slash128()
    {
        var net = Assert.Single(ClientIp.ParseTrustedProxies(["::1"]));
        Assert.Equal(128, net.PrefixLength);
        Assert.True(net.Contains(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void Blank_Entries_Are_Skipped_Not_Fatal()
        => Assert.Empty(ClientIp.ParseTrustedProxies(["", "  ", "\t"]));

    [Fact]
    public void Garbage_Fails_Loudly_And_Quotes_The_Offending_Entry()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ClientIp.ParseTrustedProxies(["10.0.0.0/8", "nginx-main"]));
        Assert.Contains("nginx-main", ex.Message);
        Assert.Contains(ClientIp.TrustedProxiesKey, ex.Message);
    }

    [Fact]
    public void Of_Reads_The_Resolved_Remote_Address()
    {
        var ctx = new DefaultHttpContext();
        Assert.Equal(ClientIp.Unknown, ClientIp.Of(ctx)); // 没来源时是固定哨兵，绝不能是 null
        Assert.Equal(ClientIp.Unknown, ClientIp.PartitionKey(ctx));

        ctx.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        Assert.Equal("203.0.113.7", ClientIp.Of(ctx));
        Assert.Equal("203.0.113.7", ClientIp.PartitionKey(ctx));
    }
}
