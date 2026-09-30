using System.ComponentModel.DataAnnotations;
using Panshi.Model.Validation;
using Panshi.Model.Enums;

namespace Panshi.Model.Dtos;

// ---------------- 部门 ----------------
public class DeptDto
{
    public string Id { get; set; } = "";

    public string? ParentId { get; set; }

    public string DeptCode { get; set; } = "";

    public string DeptName { get; set; } = "";

    public string? Leader { get; set; }

    public string? LeaderUserId { get; set; }

    public int Sort { get; set; }

    public int Status { get; set; }

    public List<DeptDto> Children { get; set; } = [];

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class DeptSaveDto
{
    public long? ParentId { get; set; }

    [Required, StringLength(64)]
    public string DeptCode { get; set; } = "";

    [Required, StringLength(64)]
    public string DeptName { get; set; } = "";

    [StringLength(64)]
    public string? Leader { get; set; }

    public long? LeaderUserId { get; set; }

    public int Sort { get; set; }

    public int Status { get; set; }

    public int Version { get; set; }
}

// ---------------- 岗位 ----------------
public class PositionQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public int? Status { get; set; }
}

public class PositionDto
{
    public string Id { get; set; } = "";

    public string PositionCode { get; set; } = "";

    public string PositionName { get; set; } = "";

    public int Sort { get; set; }

    public int Status { get; set; }

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class PositionSaveDto
{
    [Required, StringLength(64)]
    public string PositionCode { get; set; } = "";

    [Required, StringLength(64)]
    public string PositionName { get; set; } = "";

    public int Sort { get; set; }

    public int Status { get; set; }

    [StringLength(512)]
    public string? Remark { get; set; }

    public int Version { get; set; }
}

// ---------------- 字典 ----------------
public class DictTypeQuery : PagedQuery
{
    public string? Keyword { get; set; }
}

public class DictTypeDto
{
    public string Id { get; set; } = "";

    public string DictName { get; set; } = "";

    public string DictCode { get; set; } = "";

    public string? Remark { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class DictTypeSaveDto
{
    [Required, StringLength(64)]
    public string DictName { get; set; } = "";

    [Required, StringLength(64)]
    public string DictCode { get; set; } = "";

    [StringLength(512)]
    public string? Remark { get; set; }

    public int Version { get; set; }
}

public class DictDataDto
{
    public string Id { get; set; } = "";

    public string DictTypeId { get; set; } = "";

    public string Label { get; set; } = "";

    public string Value { get; set; } = "";

    public int Sort { get; set; }

    public int Status { get; set; }

    public string? TagType { get; set; }

    public bool IsDefault { get; set; }

    public int Version { get; set; }
}

public class DictDataSaveDto
{
    [Required]
    public long DictTypeId { get; set; }

    [Required, StringLength(128)]
    public string Label { get; set; } = "";

    [Required, StringLength(128)]
    public string Value { get; set; } = "";

    public int Sort { get; set; }

    public int Status { get; set; }

    [StringLength(32)]
    public string? TagType { get; set; }

    public bool IsDefault { get; set; }

    public int Version { get; set; }
}

// ---------------- 参数 ----------------
public class ConfigQuery : PagedQuery
{
    public string? Keyword { get; set; }
}

public class ConfigDto
{
    public string Id { get; set; } = "";

    public string ConfigName { get; set; } = "";

    public string ConfigKey { get; set; } = "";

    public string? ConfigValue { get; set; }

    public bool BuiltIn { get; set; }

    public string? Remark { get; set; }

    public int Version { get; set; }
}

public class ConfigSaveDto
{
    [Required, StringLength(64)]
    public string ConfigName { get; set; } = "";

    [Required, StringLength(128)]
    public string ConfigKey { get; set; } = "";

    [StringLength(1024)]
    public string? ConfigValue { get; set; }

    [StringLength(512)]
    public string? Remark { get; set; }

    public int Version { get; set; }
}

// ---------------- 公告 ----------------
public class NoticeQuery : PagedQuery
{
    public string? Title { get; set; }

    public NoticeType? NoticeType { get; set; }

    public NoticeStatus? Status { get; set; }
}

public class NoticeDto
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public NoticeType NoticeType { get; set; }

    public string? Content { get; set; }

    public NoticeStatus Status { get; set; }

    public DateTime? PublishTime { get; set; }

    public string? CreateByName { get; set; }

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class NoticeSaveDto
{
    [Required, StringLength(256)]
    public string Title { get; set; } = "";

    public NoticeType NoticeType { get; set; } = NoticeType.Notification;

    /// <summary>富文本 HTML（入库前 DOMPurify 净化——前端负责，服务端二次防护见 LogService 渲染侧）</summary>
    public string? Content { get; set; }

    public NoticeStatus Status { get; set; }

    public DateTime? PublishTime { get; set; }

    public int Version { get; set; }
}

// ---------------- 站内信 ----------------
public class MessageSendDto
{
    [Required]
    public List<long> ReceiverIds { get; set; } = [];

    [Required, StringLength(256)]
    public string Title { get; set; } = "";

    public string? Content { get; set; }
}

public class MessageQuery : PagedQuery
{
    public bool? IsRead { get; set; }

    public MessageType? MsgType { get; set; }

    /// <summary>关键字：命中 标题 / 内容 / 发送人 任一即算（LIKE 包含匹配）。</summary>
    public string? Keyword { get; set; }
}

public class MessageDto
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string? Content { get; set; }

    public MessageType MsgType { get; set; }

    public string? BizType { get; set; }

    public string? BizId { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadTime { get; set; }

    public string? SenderName { get; set; }

    public DateTime CreateTime { get; set; }
}

// ---------------- 日志 ----------------
public class OperLogQuery : PagedQuery
{
    public string? Module { get; set; }

    public string? UserName { get; set; }

    public bool? Success { get; set; }

    public DateTime? Begin { get; set; }

    public DateTime? End { get; set; }
}

public class LoginLogQuery : PagedQuery
{
    public string? UserName { get; set; }

    public bool? Success { get; set; }

    public DateTime? Begin { get; set; }

    public DateTime? End { get; set; }
}

public class ChangeLogQuery : PagedQuery
{
    public string? TableName { get; set; }

    public string? UserName { get; set; }

    public string? RecordId { get; set; }

    /// <summary>区间与登录/操作日志同约定：前端给本地时区的 startOf/endOf day，后端不再放宽。</summary>
    public DateTime? Begin { get; set; }

    public DateTime? End { get; set; }
}

public class OperLogDto
{
    public string Id { get; set; } = "";

    public string Module { get; set; } = "";

    public string Action { get; set; } = "";

    public string Method { get; set; } = "";

    public string Url { get; set; } = "";

    public string? Params { get; set; }

    public string? UserName { get; set; }

    public string? Ip { get; set; }

    public long ElapsedMs { get; set; }

    public bool Success { get; set; }

    public string? ErrorMsg { get; set; }

    public DateTime CreateTime { get; set; }
}

public class LoginLogDto
{
    public string Id { get; set; } = "";

    public string UserName { get; set; } = "";

    public string Result { get; set; } = "";

    public bool Success { get; set; }

    public string? Ip { get; set; }

    public string? UserAgent { get; set; }

    public DateTime CreateTime { get; set; }
}

public class ChangeLogDto
{
    public string Id { get; set; } = "";

    public string TableName { get; set; } = "";

    public string RecordId { get; set; } = "";

    public string Changes { get; set; } = "[]";

    public string UserName { get; set; } = "";

    public DateTime CreateTime { get; set; }
}
