using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Configuration;
using Panshi.Common.Cache;
using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;

namespace Panshi.Service.Sys;

/// <summary>判定结论。Matched 为真表示命中某条规则；Allowed 为假才拦（DryRun 时命中也只记不拦）。</summary>
public sealed record IpGuardDecision(bool Allowed, bool Matched, long RuleId, string? Cidr, IpRuleKind? Kind)
{
    /// <summary>未命中任何规则（含功能关闭、地址解析不了）。</summary>
    public static readonly IpGuardDecision Pass = new(true, false, 0, null, null);
}

/// <summary>
/// IP 黑白名单（安全 P1）。
/// 判定顺序：白 → 黑 → 未命中。白名单同时用于豁免限流（运维跳板机、监控探针不该被自己挡住）。
/// 规则整表常驻缓存（IP 规则天然量小），任何写操作后 InvalidateAll 立即生效；
/// 命中计数只在「命中」这一条稀有路径上按主键单行回写，不会把名单变成每请求的串行点。
/// </summary>
public class IpGuardService(
    IRepository<SysIpRule> ruleRepo,
    ICacheService cache,
    IConfiguration config) : BaseService<SysIpRule>(ruleRepo)
{
    private static long _version = 1;

    /// <summary>名单变更后立即失效（与 PermissionService 同一套版本键方案）。</summary>
    public static void InvalidateAll() => Interlocked.Increment(ref _version);

    /// <summary>DryRun：只记录不拦。默认开——先看清会不会误杀，再决定放行拦截。</summary>
    public bool DryRun => config.GetValue("Security:IpGuard:DryRun", true);

    public bool Enabled => config.GetValue("Security:IpGuard:Enabled", true);

    private sealed record Rule(long Id, IPNetwork Net, string Cidr);

    private sealed record Snapshot(Rule[] White, Rule[] Black);

    private async Task<Snapshot> LoadAsync()
    {
        var key = $"ipguard:rules:{_version}";
        return (await cache.GetAsync<Snapshot>(key, async () =>
        {
            var now = DateTime.Now;
            var rows = await ruleRepo.ListAsync(r => r.Status == EnableStatus.Enabled);
            var live = rows.Where(r => r.ExpiresTime is null || r.ExpiresTime > now).ToList();

            // 解析不了的规则（历史脏数据/上游改过格式）直接跳过并留给日志，绝不因为一条坏规则把整站判定打挂
            Rule[] Pick(IpRuleKind kind) => live.Where(r => r.Kind == kind)
                .Select(r => IPNetwork.TryParse(Normalize(r.Cidr), out var net)
                    ? new Rule(r.Id, net, r.Cidr) : null)
                .Where(x => x is not null).Select(x => x!).ToArray();

            return new Snapshot(Pick(IpRuleKind.White), Pick(IpRuleKind.Black));
        }, TimeSpan.FromMinutes(10)))!;
    }

    /// <summary>裸 IP 补 /32 或 /128；已是 CIDR 原样。</summary>
    public static string Normalize(string cidr)
    {
        var text = cidr.Trim();
        return text.Contains('/') ? text : text + (text.Contains(':') ? "/128" : "/32");
    }

    /// <summary>核心判定。ip 解析不了时按「未命中」放行——交给限流层的 unknown 分区键兜底，不在此处误拦。</summary>
    public async Task<IpGuardDecision> EvaluateAsync(string ip)
    {
        if (!Enabled || !IPAddress.TryParse(ip, out var addr)) return IpGuardDecision.Pass;

        var snap = await LoadAsync();
        // 白名单先查：命中即放行，且不必再看黑名单（这就是「白 > 黑」的优先级实现处）
        var white = snap.White.FirstOrDefault(w => w.Net.Contains(addr));
        if (white is not null)
        {
            TrackHit(white.Id);
            return new IpGuardDecision(true, true, white.Id, white.Cidr, IpRuleKind.White);
        }

        var black = snap.Black.FirstOrDefault(b => b.Net.Contains(addr));
        if (black is null) return IpGuardDecision.Pass;

        TrackHit(black.Id);
        return new IpGuardDecision(DryRun, true, black.Id, black.Cidr, IpRuleKind.Black);
    }

    // ---- 命中计数：先攒内存，命中是稀有路径，落库交给 FlushHits（由列表查询与作业触发） ----

    private static readonly ConcurrentDictionary<long, (long Count, DateTime Last)> Hits = new();

    private static void TrackHit(long ruleId) => Hits.AddOrUpdate(ruleId, _ => (1, DateTime.Now), (_, v) => (v.Count + 1, DateTime.Now));

    /// <summary>把内存里的命中数回写入库。列表页展示前调一次，所以不需要额外的定时器。</summary>
    public async Task FlushHitsAsync()
    {
        if (Hits.IsEmpty) return;
        foreach (var (id, hit) in Hits.ToArray())
        {
            if (!Hits.TryRemove(id, out _)) continue;
            // 规则可能在这期间被删掉了：找不到就丢弃这批计数，绝不为记账把请求带崩
            var row = await ruleRepo.FindAsync(id);
            if (row is null) continue;
            row.HitCount += hit.Count;
            row.LastHitTime = hit.Last;
            await ruleRepo.UpdateColumnsAsync(row, "HitCount", "LastHitTime");
        }
    }

    // ---- 管理侧 ----

    public async Task<PagedResult<IpRuleDto>> PageAsync(IpRuleQuery query)
    {
        await FlushHitsAsync();
        var kw = query.Keyword?.Trim();
        var exp = SqlSugar.Expressionable.Create<SysIpRule>();
        if (!string.IsNullOrEmpty(kw)) exp.And(r => r.Cidr.Contains(kw) || (r.Reason != null && r.Reason.Contains(kw)));
        if (query.Kind is IpRuleKind kind) exp.And(r => r.Kind == kind);
        if (query.Status is EnableStatus st) exp.And(r => r.Status == st);

        var page = await ruleRepo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, "create_time");
        return new PagedResult<IpRuleDto>
        {
            Total = page.Total, Rows = page.Rows.Select(ToDto).ToList()
        };
    }

    public async Task<IpRuleDto> CreateAsync(IpRuleSaveDto dto)
    {
        var cidr = Normalize(dto.Cidr);
        if (!IPNetwork.TryParse(cidr, out var net))
            throw new BizException($"\"{dto.Cidr}\" 不是合法的 IP 或 CIDR（例：203.0.113.7 或 172.16.0.0/12）");

        // 只拦黑名单：白名单只是放权，覆盖到代理不会把自己锁在外面。
        // （UpdateAsync 早就是这个口径，这里曾无条件拦，两处不一致。）
        if (dto.Kind == IpRuleKind.Black) GuardNotSelfDefeating(net);
        var now = DateTime.Now;
        var row = new SysIpRule
        {
            Cidr = cidr, Kind = dto.Kind, Source = IpRuleSource.Manual, Status = dto.Status,
            Reason = dto.Reason, ExpiresTime = dto.ExpiresTime, CreateTime = now
        };
        await ruleRepo.InsertAsync(row);
        InvalidateAll();
        return ToDto(row);
    }

    public async Task UpdateAsync(long id, IpRuleSaveDto dto)
    {
        var row = await ruleRepo.GetAsync(id);
        var cidr = Normalize(dto.Cidr);
        if (!IPNetwork.TryParse(cidr, out var net))
            throw new BizException($"\"{dto.Cidr}\" 不是合法的 IP 或 CIDR");

        // 只在「改成黑名单」时校验安全栏：白名单加宽不会自锁
        if (dto.Kind == IpRuleKind.Black) GuardNotSelfDefeating(net);

        row.Cidr = cidr;
        row.Kind = dto.Kind;
        row.Status = dto.Status;
        row.Reason = dto.Reason;
        row.ExpiresTime = dto.ExpiresTime;
        row.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(row);
        InvalidateAll();
    }

    public async Task DeleteAsync(long id)
    {
        await ruleRepo.SoftDeleteAsync(id);
        InvalidateAll();
    }

    /// <summary>
    /// 防自锁安全栏。拦两类必然出事的写法：
    /// ① 默认路由（0.0.0.0/0、::/0）——等于拒绝所有人，包括你自己；
    /// ② 覆盖到受信代理/网关的地址——直连或容器 NAT 拓扑下所有客户端都显示成这一个地址，
    ///    封它就是封全站（这正是本仓库实测到的 ip=::ffff:172.19.0.1）。
    /// </summary>
    private void GuardNotSelfDefeating(IPNetwork net)
    {
        if (net.PrefixLength == 0)
            throw new BizException("不允许封禁默认路由（/0）：那会拒绝包括你在内的所有来源");

        foreach (var proxy in ReadTrustedProxies())
        {
            // 相交判定必须双向：只问「规则含不含代理首地址」会漏掉 127.0.0.1/32 这种
            // 「规则比保护段小」的情形（首地址 127.0.0.0 根本不在 /32 里），而那恰恰是最常见的误封写法。
            if (net.Contains(proxy.BaseAddress) || proxy.Contains(net.BaseAddress))
                throw new BizException(
                    $"该规则覆盖受信代理/来源地址 {proxy}，封掉它等于封掉所有客户端" +
                    "（直连或容器 NAT 拓扑下所有请求都显示为同一个来源）。请先解决真实来源 IP，再谈黑名单。");
        }
    }

    private IEnumerable<IPNetwork> ReadTrustedProxies()
    {
        var arr = config.GetSection("Security:TrustedProxies").Get<string[]>();
        var inline = config["Security:TrustedProxies"];
        var entries = arr is { Length: > 0 } ? arr : (inline ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var raw in entries)
        {
            var text = Normalize(raw.Trim());
            if (IPNetwork.TryParse(text, out var net)) yield return net;
        }

        // 容器/开发机最常见的共享来源：本机回环与常见 docker 网关段。没有配 TrustedProxies 时也要挡住。
        foreach (var fallback in new[] { "127.0.0.0/8", "172.17.0.0/16", "172.19.0.0/16" })
            if (IPNetwork.TryParse(fallback, out var f)) yield return f;
    }

    private static IpRuleDto ToDto(SysIpRule r) => new()
    {
        Id = r.Id.ToString(), Cidr = r.Cidr, Kind = r.Kind, Source = r.Source, Status = r.Status,
        Reason = r.Reason, ExpiresTime = r.ExpiresTime, HitCount = r.HitCount.ToString(),
        LastHitTime = r.LastHitTime, CreateTime = r.CreateTime, Version = r.Version
    };
}
