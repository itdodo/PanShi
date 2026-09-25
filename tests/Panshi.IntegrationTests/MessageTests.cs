using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Sys;
using SqlSugar;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 站内信收件箱：关键字搜索（标题/内容/发送人）、单条软删（含越权与幂等）、清空已读只动已读。
/// 每个用例用独立 ReceiverId 隔离，避免与种子/其他用例的消息互扰。
/// </summary>
[Collection("pg")]
public class MessageTests(PgFixture fx) : PgTestBase(fx)
{
    private MessageService Svc() => new(Fx.Repo<SysMessage>(), new RecordingNotify());

    /// <summary>插入一条属于 uid 的消息，返回其 id。</summary>
    private async Task<long> Put(long uid, string title, string? content, bool isRead, string? sender = null)
    {
        using var _ = As(uid, "probe", null);
        var m = await Fx.Repo<SysMessage>().InsertAsync(new SysMessage
        {
            ReceiverId = uid,
            Title = title,
            Content = content,
            MsgType = MessageType.InSite,
            IsRead = isRead,
            ReadTime = isRead ? DateTime.Now : null,
            SenderName = sender
        });
        return m.Id;
    }

    private static MessageQuery Q(long uid, string? kw = null, bool? isRead = null) => new()
    {
        PageNum = 1,
        PageSize = 50,
        Keyword = kw,
        IsRead = isRead
    };

    [Fact]
    public async Task MyPage_Keyword_Matches_Title_Content_And_SenderName()
    {
        long uid = SnowflakeId.NextId();
        await Put(uid, "季度预算评审", "请在周五前提交材料", false, "王五");
        await Put(uid, "例会改期", "会议室调整通知", false, "赵六");

        var svc = Svc();
        Assert.Equal(1, (await svc.MyPageAsync(uid, Q(uid, "预算"))).Total);
        Assert.Equal(1, (await svc.MyPageAsync(uid, Q(uid, "会议室调整"))).Total); // 命中内容
        Assert.Equal(1, (await svc.MyPageAsync(uid, Q(uid, "赵六"))).Total);      // 命中发送人
        Assert.Equal(2, (await svc.MyPageAsync(uid, Q(uid))).Total);              // 不填关键字=全量
        Assert.Equal(0, (await svc.MyPageAsync(uid, Q(uid, "不存在的关键字"))).Total);
    }

    [Fact]
    public async Task MyPage_Keyword_Combines_With_Read_Filter()
    {
        long uid = SnowflakeId.NextId();
        await Put(uid, "项目周报", null, true);
        await Put(uid, "项目月报", null, false);

        var svc = Svc();
        Assert.Equal(2, (await svc.MyPageAsync(uid, Q(uid, "项目"))).Total);
        Assert.Equal(1, (await svc.MyPageAsync(uid, Q(uid, "项目", isRead: true))).Total);
        Assert.Equal(1, (await svc.MyPageAsync(uid, Q(uid, "项目", isRead: false))).Total);
    }

    [Fact]
    public async Task Delete_Removes_Own_Message_From_List_And_Unread()
    {
        long uid = SnowflakeId.NextId();
        var id = await Put(uid, "待删除消息", "正文", false);
        var svc = Svc();
        Assert.Equal(1, await svc.UnreadCountAsync(uid));

        await svc.DeleteAsync(uid, id);

        Assert.Equal(0, (await svc.MyPageAsync(uid, Q(uid))).Total);
        Assert.Equal(0, await svc.UnreadCountAsync(uid));
        // 软删：物理行仍在（ClearFilter 绕过全局 IsDeleted 过滤器才能看到），只是业务查询不再命中
        Assert.NotNull(await Db.Queryable<SysMessage>().ClearFilter().InSingleAsync(id));
    }

    [Fact]
    public async Task Delete_OtherUsers_Message_Is_Forbidden()
    {
        long owner = SnowflakeId.NextId();
        long attacker = SnowflakeId.NextId();
        var id = await Put(owner, "别人的消息", null, false);

        await Assert.ThrowsAsync<BizException>(() => Svc().DeleteAsync(attacker, id));
        Assert.Equal(1, (await Svc().MyPageAsync(owner, Q(owner))).Total); // 未被删掉
    }

    [Fact]
    public async Task Delete_Missing_Or_AlreadyDeleted_Is_Idempotent()
    {
        long uid = SnowflakeId.NextId();
        var id = await Put(uid, "会被删两次", null, false);
        var svc = Svc();

        await svc.DeleteAsync(uid, id);
        await svc.DeleteAsync(uid, id);          // 二次删除：幂等，不抛
        await svc.DeleteAsync(uid, 999_999_999); // 不存在的 id：幂等，不抛
        Assert.Equal(0, (await svc.MyPageAsync(uid, Q(uid))).Total);
    }

    [Fact]
    public async Task ClearRead_Only_Removes_Read_And_Keeps_Unread()
    {
        long uid = SnowflakeId.NextId();
        await Put(uid, "已读一条", null, true);
        await Put(uid, "已读两条", null, true);
        await Put(uid, "未读留着", null, false);

        var svc = Svc();
        Assert.Equal(2, await svc.ClearReadAsync(uid));
        Assert.Equal(0, (await svc.MyPageAsync(uid, Q(uid, null, isRead: true))).Total);
        Assert.Equal(1, (await svc.MyPageAsync(uid, Q(uid))).Total);
        Assert.Equal("未读留着", (await svc.MyPageAsync(uid, Q(uid))).Rows[0].Title);
    }

    [Fact]
    public async Task ClearRead_Does_Not_Count_AlreadySoftDeleted_Rows()
    {
        // 回归：批量软删曾漏掉 !IsDeleted 条件（全局过滤器不作用于 Updateable），
        // 会把已删行计入受影响行数并重复 UPDATE 审计字段。
        long uid = SnowflakeId.NextId();
        await Put(uid, "已读甲", null, true);
        await Put(uid, "已读乙", null, true);
        var third = await Put(uid, "已读丙", null, true);
        var svc = Svc();

        await svc.DeleteAsync(uid, third);          // 先软删一条（它仍是 is_read=true）
        Assert.Equal(2, await svc.ClearReadAsync(uid)); // 只该报剩下的 2 条，不能把上面那条重复算进去
        Assert.Equal(0, (await svc.MyPageAsync(uid, Q(uid))).Total);
    }

    [Fact]
    public async Task ClearRead_Does_Not_Touch_OtherUsers()
    {
        long a = SnowflakeId.NextId();
        long b = SnowflakeId.NextId();
        await Put(a, "甲的已读", null, true);
        await Put(b, "乙的已读", null, true);

        Assert.Equal(1, await Svc().ClearReadAsync(a));
        Assert.Equal(1, (await Svc().MyPageAsync(b, Q(b))).Total); // 乙的不受影响
    }
}
