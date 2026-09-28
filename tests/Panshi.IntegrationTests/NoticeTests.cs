using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Service.Sys;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>
/// 公告（commit 2656947 回归）：发布时间只增不清（草稿→发布→撤回→再发布），
/// 定时发布校验，以及 NoticeDto.CreateByName 四路回填。
/// ①的前端「草稿/停用」文案全靠 publishTime 的有无区分，撤回时被抹成 null 是真根因。
/// </summary>
[Collection("pg")]
public class NoticeTests(PgFixture fx) : PgTestBase(fx)
{
    private NoticeService Svc() => new(Fx.Repo<SysNotice>(), Fx.Repo<SysUser>());

    private static NoticeSaveDto Draft(string title) => new()
    {
        Title = title, NoticeType = NoticeType.Notification, Status = NoticeStatus.Stopped
    };

    private async Task<NoticeDto> Reload(string id) => await Svc().GetAsync(long.Parse(id));

    [Fact]
    public async Task PublishTime_OnlyGrows_WithdrawKeeps_RepublishKeepsFirst()
    {
        var title = $"公告回归-{Guid.NewGuid():N}";
        var created = await Svc().CreateAsync(Draft(title));

        var draft = await Reload(created.Id);
        Assert.Equal(NoticeStatus.Stopped, draft.Status);
        Assert.Null(draft.PublishTime); // 从未发布 = 前端「草稿」文案的依据

        // 发布：Apply 补首发时间
        await Svc().UpdateAsync(long.Parse(created.Id), new NoticeSaveDto
        {
            Title = title, NoticeType = NoticeType.Notification,
            Status = NoticeStatus.Published, Version = draft.Version
        });
        var published = await Reload(created.Id);
        var firstPublish = published.PublishTime;
        Assert.NotNull(firstPublish);

        // 撤回停用：发布时间保留（曾被覆盖成 null → ①文案退回「草稿」）
        await Svc().UpdateAsync(long.Parse(created.Id), new NoticeSaveDto
        {
            Title = title, NoticeType = NoticeType.Notification,
            Status = NoticeStatus.Stopped, Version = published.Version
        });
        var stopped = await Reload(created.Id);
        Assert.Equal(NoticeStatus.Stopped, stopped.Status);
        Assert.Equal(firstPublish, stopped.PublishTime);

        // 重新发布：不覆盖首发时间
        await Svc().UpdateAsync(long.Parse(created.Id), new NoticeSaveDto
        {
            Title = title, NoticeType = NoticeType.Notification,
            Status = NoticeStatus.Published, Version = stopped.Version
        });
        Assert.Equal(firstPublish, (await Reload(created.Id)).PublishTime);
    }

    [Fact]
    public async Task Scheduled_Requires_Future_Time_And_Keeps_DtoValue()
    {
        var title = $"公告定时-{Guid.NewGuid():N}";
        var svc = Svc();

        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(new NoticeSaveDto
        {
            Title = title, Status = NoticeStatus.Scheduled
        }));
        await Assert.ThrowsAsync<BizException>(() => svc.CreateAsync(new NoticeSaveDto
        {
            Title = title, Status = NoticeStatus.Scheduled, PublishTime = DateTime.Now.AddMinutes(-1)
        }));

        var when = DateTime.Now.AddMinutes(30);
        var created = await svc.CreateAsync(new NoticeSaveDto
        {
            Title = title, Status = NoticeStatus.Scheduled, PublishTime = when
        });
        Assert.Equal(NoticeStatus.Scheduled, created.Status);
        Assert.NotNull(created.PublishTime);
    }

    [Fact]
    public async Task CreateByName_Backfilled_On_Create_Get_Page_And_Latest()
    {
        // 必须显式开作用域：fixture InitializeAsync 里的 OperationUser.Use 是 AsyncLocal，不会传播到测试方法
        using var _ = As(1, "admin", 2);
        var title = $"公告署名-{Guid.NewGuid():N}";
        var svc = Svc();
        var created = await svc.CreateAsync(new NoticeSaveDto
        {
            Title = title, Status = NoticeStatus.Published
        });
        Assert.Equal("超级管理员", created.CreateByName);
        Assert.Equal("超级管理员", (await Reload(created.Id)).CreateByName);

        var page = await svc.PageAsync(new NoticeQuery { PageNum = 1, PageSize = 10, Title = title });
        Assert.Equal(1, page.Total);
        Assert.Equal("超级管理员", page.Rows[0].CreateByName);

        Assert.Contains((await svc.LatestAsync(50)), n => n.Id == created.Id && n.CreateByName == "超级管理员");
    }
}
