using Panshi.Common.Exceptions;
using Panshi.Common.Realtime;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using SqlSugar;

namespace Panshi.Service.Sys;

/// <summary>公告服务（Status：0停用 1发布 2定时发布；latest 仅返回已发布）。</summary>
public class NoticeService(IRepository<SysNotice> repo, IRepository<SysUser> userRepo) : BaseService<SysNotice>(repo)
{
    public async Task<PagedResult<NoticeDto>> PageAsync(NoticeQuery query)
    {
        var title = query.Title?.Trim();
        var exp = Expressionable.Create<SysNotice>();
        if (!string.IsNullOrEmpty(title)) exp.And(n => n.Title.Contains(title));
        if (query.NoticeType is not null) exp.And(n => n.NoticeType == query.NoticeType!.Value);
        if (query.Status is not null) exp.And(n => n.Status == query.Status!.Value);
        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        var names = await CreatorNamesAsync(page.Rows);
        return new PagedResult<NoticeDto> { Total = page.Total, Rows = page.Rows.Select(n => ToDto(n, names)).ToList() };
    }

    /// <summary>顶栏铃铛「公告」Tab：已发布前 N 条。</summary>
    public async Task<List<NoticeDto>> LatestAsync(int take = 10)
    {
        var list = (await Repo.ListAsync(n => n.Status == NoticeStatus.Published))
            .OrderByDescending(n => n.PublishTime ?? n.CreateTime).Take(take).ToList();
        var names = await CreatorNamesAsync(list);
        return list.Select(n => ToDto(n, names)).ToList();
    }

    public async Task<NoticeDto> GetAsync(long id)
    {
        var n = await Repo.GetAsync(id);
        return ToDto(n, await CreatorNamesAsync(new List<SysNotice> { n }));
    }

    public async Task<NoticeDto> CreateAsync(NoticeSaveDto dto)
    {
        Validate(dto);
        var n = Apply(new SysNotice(), dto);
        await Repo.InsertAsync(n);
        return ToDto(n, await CreatorNamesAsync(new List<SysNotice> { n }));
    }

    public async Task UpdateAsync(long id, NoticeSaveDto dto)
    {
        Validate(dto);
        var n = await Repo.GetAsync(id);
        Apply(n, dto);
        n.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(n);
    }

    public async Task DeleteAsync(long id) => await Repo.SoftDeleteAsync(id);

    private static void Validate(NoticeSaveDto dto)
    {
        if (dto.Status == NoticeStatus.Scheduled && dto.PublishTime is null)
            throw new BizException("定时发布必须指定发布时间");
        if (dto.Status == NoticeStatus.Scheduled && dto.PublishTime <= DateTime.Now)
            throw new BizException("定时发布时间必须晚于当前时间");
    }

    private static SysNotice Apply(SysNotice n, NoticeSaveDto dto)
    {
        n.Title = dto.Title;
        n.NoticeType = dto.NoticeType;
        n.Content = Sanitize(dto.Content);
        n.Status = dto.Status;
        // 发布时间只增不清：撤回停用保留首次发布时间（前端据此区分「草稿/停用」文案）；重新发布不覆盖首发时间
        n.PublishTime = dto.Status switch
        {
            NoticeStatus.Published => dto.PublishTime ?? n.PublishTime ?? DateTime.Now,
            NoticeStatus.Stopped => n.PublishTime,
            _ => dto.PublishTime
        };
        return n;
    }

    /// <summary>富文本入库净化：服务端兜底剥离 script/iframe/on* 事件（渲染端仍需 DOMPurify 二次净化——安全清单 #5）。</summary>
    public static string? Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html)) return html;
        var cleaned = System.Text.RegularExpressions.Regex.Replace(html,
            @"<\s*(script|iframe|object|embed|link|meta|style)[^>]*>.*?<\s*/\s*\1\s*>|<\s*(script|iframe|object|embed|link|meta|style)[^>]*/?>",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\son[a-z]+\s*=\s*(""[^""]*""|'[^']*'|[^\s>]+)",
            "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return cleaned;
    }

    private async Task<Dictionary<long, string>> CreatorNamesAsync(IEnumerable<SysNotice> list)
    {
        var ids = list.Select(n => n.CreateBy).Where(x => x is not null).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<long, string>();
        var users = await userRepo.ListAsync(u => ids.Contains(u.Id));
        return users.ToDictionary(u => u.Id, u => u.NickName);
    }

    private static NoticeDto ToDto(SysNotice n, Dictionary<long, string> names) => new()
    {
        Id = n.Id.ToString(), Title = n.Title, NoticeType = n.NoticeType, Content = n.Content,
        Status = n.Status, PublishTime = n.PublishTime, CreateTime = n.CreateTime, Version = n.Version,
        CreateByName = n.CreateBy is long cb ? names.GetValueOrDefault(cb) : null
    };
}

/// <summary>站内信服务（发送=落库+实时推；我的收件箱/未读数/已读）。</summary>
public class MessageService(IRepository<SysMessage> repo, INotifyService notify)
{
    public async Task SendAsync(MessageSendDto dto, long senderId, string senderName)
    {
        if (dto.ReceiverIds.Count == 0) throw new BizException("请选择接收人");
        if (dto.ReceiverIds.Count > 500) throw new BizException("单次发送不可超过 500 人");

        foreach (var uid in dto.ReceiverIds.Distinct())
            await notify.NotifyUserAsync(uid, dto.Title, dto.Content, "insite", null, null);
    }

    public async Task<PagedResult<MessageDto>> MyPageAsync(long userId, MessageQuery query)
    {
        var exp = Expressionable.Create<SysMessage>();
        exp.And(m => m.ReceiverId == userId);
        if (query.IsRead is not null) exp.And(m => m.IsRead == query.IsRead!.Value);
        if (query.MsgType is not null) exp.And(m => m.MsgType == query.MsgType!.Value);
        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            exp.And(m => m.Title.Contains(kw) || m.Content!.Contains(kw) || m.SenderName!.Contains(kw));
        }
        var (col, desc) = query.ResolveSort(new Dictionary<string, string> { ["createTime"] = "create_time" });
        var page = await repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<MessageDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    public async Task<int> UnreadCountAsync(long userId)
        => (int)await repo.CountAsync(m => m.ReceiverId == userId && !m.IsRead);

    public async Task MarkReadAsync(long userId, long id)
    {
        var m = await repo.GetAsync(id);
        if (m.ReceiverId != userId) throw BizException.Forbidden("越权操作");
        if (m.IsRead) return;
        m.IsRead = true;
        m.ReadTime = DateTime.Now;
        await repo.UpdateColumnsAsync(m, "IsRead", "ReadTime");
    }

    public async Task MarkAllReadAsync(long userId)
    {
        var unread = await repo.ListAsync(m => m.ReceiverId == userId && !m.IsRead);
        foreach (var m in unread)
        {
            m.IsRead = true;
            m.ReadTime = DateTime.Now;
        }

        if (unread.Count > 0)
            await repo.Db.Updateable(unread)
                .UpdateColumns(m => new { m.IsRead, m.ReadTime })
                .ExecuteCommandAsync();
    }

    /// <summary>删除单条收件箱消息：软删（全局 IsDeleted 过滤器随即可见性生效），只允许本人删自己的。</summary>
    public async Task DeleteAsync(long userId, long id)
    {
        // 用 FindAsync 而非 GetAsync：后者对「不存在/已删」是抛 BizException，会让二次删除报错。
        // 删除按幂等处理（多标签页并发删同一条、或刚清空已读后再点删除，都不该给用户弹错）。
        var m = await repo.FindAsync(x => x.Id == id);
        if (m is null) return;
        if (m.ReceiverId != userId) throw BizException.Forbidden("越权操作");
        await repo.SoftDeleteAsync(id);
    }

    /// <summary>清空已读：只软删本人已读消息，未读一律保留。</summary>
    public async Task<int> ClearReadAsync(long userId)
        => await repo.SoftDeleteAsync(m => m.ReceiverId == userId && m.IsRead);

    static MessageDto ToDto(SysMessage m) => new()
    {
        Id = m.Id.ToString(), Title = m.Title, Content = m.Content, MsgType = m.MsgType,
        BizType = m.BizType, BizId = m.BizId?.ToString(), IsRead = m.IsRead, ReadTime = m.ReadTime,
        SenderName = m.SenderName, CreateTime = m.CreateTime
    };
}
