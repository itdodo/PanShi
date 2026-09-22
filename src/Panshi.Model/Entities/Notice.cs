using Panshi.Model.Enums;
using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>公告（Content 富文本 HTML——入库前 DOMPurify 净化；定时发布由作业翻状态）</summary>
[SugarTable("sys_notice")]
public class SysNotice : BaseEntity
{
    [SugarColumn(Length = 256)]
    public string Title { get; set; } = "";

    public NoticeType NoticeType { get; set; } = NoticeType.Notification;

    /// <summary>富文本 HTML（渲染端再次净化）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "text")]
    public string? Content { get; set; }

    /// <summary>0停用 1发布 2定时发布</summary>
    public NoticeStatus Status { get; set; } = NoticeStatus.Stopped;

    /// <summary>定时发布时间（Status=2 时必填，到期 2→1）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "timestamp")]
    public DateTime? PublishTime { get; set; }
}

/// <summary>站内信（INotifyService 双写落库 + SignalR 推送）</summary>
[SugarTable("sys_message")]
public class SysMessage : BaseEntity
{
    /// <summary>接收人</summary>
    [SugarColumn(ColumnDataType = "bigint")]
    public long ReceiverId { get; set; }

    [SugarColumn(Length = 256)]
    public string Title { get; set; } = "";

    [SugarColumn(IsNullable = true, ColumnDataType = "text")]
    public string? Content { get; set; }

    public MessageType MsgType { get; set; } = MessageType.System;

    /// <summary>业务类型（前端跳转路由映射键，如 flow-task/flow-cc/notice）</summary>
    [SugarColumn(IsNullable = true, Length = 64)]
    public string? BizType { get; set; }

    /// <summary>业务对象 Id（流程实例/公告等）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? BizId { get; set; }

    public bool IsRead { get; set; }

    [SugarColumn(IsNullable = true, ColumnDataType = "timestamp")]
    public DateTime? ReadTime { get; set; }

    /// <summary>发送人（系统消息为 null）</summary>
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? SenderId { get; set; }

    [SugarColumn(IsNullable = true, Length = 64)]
    public string? SenderName { get; set; }
}
