using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Sys;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// IP 黑白名单判定（安全 P1）。这里钉的是四类会出事的性质：
/// ① 黑名单真能拦、DryRun 真不拦；② 白名单优先于黑名单；
/// ③ 过期/停用规则不得再生效；④ 防自锁安全栏必须挡住「封掉网关/回环/默认路由」这类写法。
/// </summary>
[Collection("pg")]
public class IpGuardTests(PgFixture fx) : PgTestBase(fx)
{
    private static string Tag() => "ip" + SnowflakeId.NextId();

    private async Task<IpRuleDto> AddAsync(IpGuardService svc, string cidr, IpRuleKind kind,
        DateTime? expires = null, EnableStatus status = EnableStatus.Enabled)
        => await svc.CreateAsync(new IpRuleSaveDto
        {
            Cidr = cidr, Kind = kind, Status = status, ExpiresTime = expires, Reason = "IpGuard 回归"
        });

    private async Task CleanAsync(string tag)
        => await Db.Deleteable<SysIpRule>().Where(r => r.Reason == "IpGuard 回归").ExecuteCommandAsync();

    [Fact]
    public async Task Blacklist_Blocks_When_DryRun_Off()
    {
        var svc = Fx.IpGuard(dryRun: false);
        await AddAsync(svc, "203.0.113.77", IpRuleKind.Black);
        try
        {
            var d = await svc.EvaluateAsync("203.0.113.77");
            Assert.True(d.Matched);
            Assert.False(d.Allowed);
            Assert.Equal(IpRuleKind.Black, d.Kind);
            Assert.True((await svc.EvaluateAsync("203.0.113.78")).Allowed); // 差一个地址就不该命中
        }
        finally
        {
            await CleanAsync(Tag());
        }
    }

    [Fact]
    public async Task Blacklist_Only_Logs_When_DryRun_On()
    {
        var svc = Fx.IpGuard(dryRun: true);
        await AddAsync(svc, "203.0.113.79/32", IpRuleKind.Black);
        try
        {
            var d = await svc.EvaluateAsync("203.0.113.79");
            Assert.True(d.Matched); // 命中了，所以会留痕
            Assert.True(d.Allowed); // 但 DryRun 不拦
        }
        finally
        {
            await CleanAsync(Tag());
        }
    }

    [Fact]
    public async Task Whitelist_Beats_Blacklist_On_Overlapping_Range()
    {
        var svc = Fx.IpGuard(dryRun: false);
        await AddAsync(svc, "10.0.0.0/8", IpRuleKind.White);
        await AddAsync(svc, "10.1.2.3", IpRuleKind.Black);
        try
        {
            var d = await svc.EvaluateAsync("10.1.2.3");
            Assert.Equal(IpRuleKind.White, d.Kind);
            Assert.True(d.Allowed);
        }
        finally
        {
            await CleanAsync(Tag());
        }
    }

    [Fact]
    public async Task Expired_And_Disabled_Rules_Are_Inert()
    {
        var svc = Fx.IpGuard(dryRun: false);
        await AddAsync(svc, "203.0.113.90", IpRuleKind.Black, expires: DateTime.Now.AddMinutes(-1));
        await AddAsync(svc, "203.0.113.91", IpRuleKind.Black, status: EnableStatus.Disabled);
        try
        {
            Assert.False((await svc.EvaluateAsync("203.0.113.90")).Matched);
            Assert.False((await svc.EvaluateAsync("203.0.113.91")).Matched);
        }
        finally
        {
            await CleanAsync(Tag());
        }
    }

    [Fact]
    public async Task New_Rule_Takes_Effect_Without_Restart()
    {
        // 缓存按版本号整体失效，漏了 InvalidateAll 的话这里会读到旧快照——静默不生效是最难查的那类 bug
        var svc = Fx.IpGuard(dryRun: false);
        Assert.False((await svc.EvaluateAsync("198.51.100.4")).Matched);
        await AddAsync(svc, "198.51.100.4", IpRuleKind.Black);
        try
        {
            Assert.False((await svc.EvaluateAsync("198.51.100.4")).Allowed);
        }
        finally
        {
            await CleanAsync(Tag());
        }
    }

    [Theory]
    [InlineData("0.0.0.0/0")] // 默认路由：封了就是拒绝所有人
    [InlineData("::/0")]
    [InlineData("127.0.0.1")] // 回环段（内置兜底名单）
    [InlineData("172.19.0.1")] // 容器网关（内置兜底名单）
    [InlineData("172.19.0.0/16")]
    public async Task Rail_Rejects_Rules_That_Would_Lock_Everyone_Out(string cidr)
        => await Assert.ThrowsAsync<BizException>(() =>
            Fx.IpGuard(dryRun: false).CreateAsync(new IpRuleSaveDto { Cidr = cidr, Kind = IpRuleKind.Black }));

    [Fact]
    public async Task Rail_Also_Guards_Configured_Trusted_Proxy()
    {
        var svc = Fx.IpGuard(dryRun: false, trustedProxies: ["192.0.2.50"]);
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(new IpRuleSaveDto
        {
            Cidr = "192.0.2.0/24", Kind = IpRuleKind.Black
        }));
        // 同一条规则改成白名单就该放过：加宽不会自锁
        var ok = await svc.CreateAsync(new IpRuleSaveDto { Cidr = "192.0.2.0/24", Kind = IpRuleKind.White });
        Assert.Equal("192.0.2.0/24", ok.Cidr);
        await Db.Deleteable<SysIpRule>().Where(r => r.Reason == "IpGuard 回归").ExecuteCommandAsync();
    }

    [Fact]
    public async Task Invalid_Cidr_Is_Rejected_With_A_Readable_Message()
    {
        var svc = Fx.IpGuard(dryRun: false);
        var ex = await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(new IpRuleSaveDto
        {
            Cidr = "not-an-ip", Kind = IpRuleKind.Black
        }));
        Assert.Contains("not-an-ip", ex.Message);
    }

    [Fact]
    public async Task Bare_Ipv4_Is_Stored_Normalized_As_Slash32()
    {
        var svc = Fx.IpGuard(dryRun: false);
        var created = await AddAsync(svc, "198.51.100.77", IpRuleKind.White);
        try
        {
            Assert.Equal("198.51.100.77/32", created.Cidr);
            Assert.True((await svc.EvaluateAsync("198.51.100.77")).Allowed);
        }
        finally
        {
            await CleanAsync(Tag());
        }
    }
}
