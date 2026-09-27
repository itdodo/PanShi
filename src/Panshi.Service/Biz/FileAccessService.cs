using Panshi.Common.Exceptions;
using Panshi.Model.Entities;
using Panshi.Repository;
using Panshi.Service.Base;

namespace Panshi.Service.Biz;

/// <summary>
/// 文件读取归属判定。/file/{id} 与 /file/{id}/download 原先只要登录就能读任意文件，
/// 而雪花 id 单调递增、可枚举——报销/采购附件不能这样暴露。
/// 规则（自上而下短路）：上传者本人 → 头像（本来就要给同事看）→ All 档（能看全部单据，附件自然能看）
/// → 能看见「引用了这个文件的那张单据」的人。
/// </summary>
public class FileAccessService(
    IRepository<SysFile> fileRepo,
    IRepository<BizExpense> expenseRepo,
    IRepository<BizPurchaseRequest> purchaseRepo,
    DataScopeService dataScope)
{
    public async Task EnsureReadableAsync(long fileId, long userId)
    {
        var file = await fileRepo.FindAsync(fileId) ?? throw BizException.NotFound("文件");
        if (file.UploaderId == userId) return;
        if (file.BizType == "avatar") return;

        var ctx = await dataScope.ResolveAsync(userId);
        if (ctx is null) return;

        // 附件 id 以 JSON 字符串数组存列里，按 "id" 带引号匹配才不会误命中 12 命中 123
        var key = $"\"{fileId}\"";
        var expenses = await expenseRepo.ListAsync(x => x.AttachmentIds != null && x.AttachmentIds.Contains(key));
        if (expenses.Any(x => DataScopeService.IsVisible(ctx, x))) return;
        var purchases = await purchaseRepo.ListAsync(x => x.AttachmentIds != null && x.AttachmentIds.Contains(key));
        if (purchases.Any(x => DataScopeService.IsVisible(ctx, x))) return;

        throw BizException.Forbidden("无权访问该文件");
    }
}
