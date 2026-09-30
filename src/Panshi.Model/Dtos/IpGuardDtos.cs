using System.ComponentModel.DataAnnotations;
using Panshi.Model.Enums;

namespace Panshi.Model.Dtos;

/// <summary>IP 名单行。雪花 id 一律字符串（红线：前端 id 用 string）。</summary>
public class IpRuleDto
{
    public string Id { get; set; } = "";

    public string Cidr { get; set; } = "";

    public IpRuleKind Kind { get; set; }

    public IpRuleSource Source { get; set; }

    public EnableStatus Status { get; set; }

    public string? Reason { get; set; }

    public DateTime? ExpiresTime { get; set; }

    /// <summary>命中次数（同样按字符串回传，避免前端 Number 精度截断）</summary>
    public string HitCount { get; set; } = "0";

    public DateTime? LastHitTime { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

/// <summary>新增/编辑 IP 名单。来源固定为人工，自动项由作动层自己写入，不开这个口子。</summary>
public class IpRuleSaveDto
{
    [Required, StringLength(64, MinimumLength = 2)]
    public string Cidr { get; set; } = "";

    public IpRuleKind Kind { get; set; } = IpRuleKind.Black;

    public EnableStatus Status { get; set; } = EnableStatus.Enabled;

    [StringLength(256)]
    public string? Reason { get; set; }

    public DateTime? ExpiresTime { get; set; }

    /// <summary>乐观锁回显值（红线 #6）</summary>
    public int Version { get; set; }
}

/// <summary>IP 名单查询。</summary>
public class IpRuleQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public IpRuleKind? Kind { get; set; }

    public EnableStatus? Status { get; set; }
}
