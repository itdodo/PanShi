using Panshi.Model.Enums;
using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>
/// IP 黑白名单规则（安全 P1）。Cidr 允许裸 IP（自动按 /32 或 /128 处理）或 CIDR 段。
/// 判定顺序：白名单命中即放行（含豁免限流）→ 黑名单命中即拒 → 都不命中则交给后续管道。
/// ⚠️ 名单里的地址是「解析后的来源」，直连/容器 NAT 拓扑下那是网关——IpGuardService 有硬安全栏，
/// 禁止写入会命中受信代理/网关的规则，否则一拉黑就是拉黑所有人。
/// </summary>
[SugarTable("sys_ip_rule")]
public class SysIpRule : BaseEntity
{
    /// <summary>IP 或 CIDR（如 203.0.113.7 / 172.16.0.0/12）</summary>
    [SugarColumn(Length = 64)]
    public string Cidr { get; set; } = "";

    /// <summary>1 黑名单（拒绝）/ 2 白名单（放行且豁免限流）</summary>
    public IpRuleKind Kind { get; set; } = IpRuleKind.Black;

    /// <summary>1 人工配置 / 2 系统自动（自动项一律带过期时间，不许永久）</summary>
    public IpRuleSource Source { get; set; } = IpRuleSource.Manual;

    /// <summary>启用开关（先停用而不删，便于快速回滚一次误封）</summary>
    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    /// <summary>封禁原因/备注（进操作日志与列表展示，也是事后追责的唯一线索）</summary>
    [SugarColumn(IsNullable = true, Length = 256)]
    public string? Reason { get; set; }

    /// <summary>过期时间；null = 永久（仅人工项允许）。到期即视为不存在。</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "timestamp")]
    public DateTime? ExpiresTime { get; set; }

    /// <summary>命中次数（采样异步回写，见 IpGuardService 的说明）</summary>
    [SugarColumn(ColumnDataType = "bigint")]
    public long HitCount { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "timestamp")]
    public DateTime? LastHitTime { get; set; }
}
