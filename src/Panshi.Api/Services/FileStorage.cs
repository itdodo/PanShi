using Panshi.Common.Exceptions;
using Panshi.Common.Security;
using Panshi.Model.Entities;
using Panshi.Repository;

namespace Panshi.Api.Services;

/// <summary>
/// 本地磁盘存储（安全清单 #4）：扩展名白名单 + 大小限制 + magic bytes 嗅探。
/// 落盘目录 uploads/{yyyyMM}/{storeName}；元数据进 sys_file。
/// </summary>
public class FileStorage(IConfiguration config)
{
    public string Root => Path.Combine(AppContext.BaseDirectory,
        config["Db:UploadDir"] ?? "uploads");

    /// <summary>默认白名单（头像/文档/图片）；上限 10MB。</summary>
    public static readonly HashSet<string> DefaultAllowed =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".pdf",
            ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            ".txt", ".csv", ".md", ".json", ".zip"
        };

    public async Task<SysFile> SaveAsync(IFormFile file, string? bizType = null,
        ISet<string>? allowed = null, long maxBytes = 10 * 1024 * 1024)
    {
        var ext = Path.GetExtension(file.FileName);
        var whitelist = allowed ?? DefaultAllowed;
        if (string.IsNullOrEmpty(ext) || !whitelist.Contains(ext))
            throw new BizException($"不支持的文件类型：{ext}");
        if (file.Length > maxBytes)
            throw new BizException($"文件超过大小限制 {maxBytes / 1024 / 1024}MB");

        byte[] head = new byte[16];
        await using (var probe = file.OpenReadStream())
        {
            var read = await probe.ReadAsync(head);
            head = head[..Math.Max(read, 0)];
        }

        var error = FileSignature.Validate(ext, head, file.Length);
        if (error is not null) throw new BizException(error);

        var storeName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var month = DateTime.Now.ToString("yyyyMM");
        var dir = Path.Combine(Root, month);
        Directory.CreateDirectory(dir);
        await using (var fs = new FileStream(Path.Combine(dir, storeName), FileMode.CreateNew))
        {
            await file.CopyToAsync(fs);
        }

        return new SysFile
        {
            FileName = Path.GetFileName(file.FileName),
            StoreName = $"{month}/{storeName}",
            ContentType = file.ContentType,
            Size = file.Length,
            BizType = bizType,
            UploaderId = OperationUserScope.CurrentUserId(),
            UploaderName = Common.Runtime.OperationUser.UserName
        };
    }

    /// <summary>物理路径（含目录逃逸防护——storeName 必须为 yyyyMM/guid.ext 形态）。</summary>
    public string ResolvePath(SysFile meta)
    {
        var path = Path.GetFullPath(Path.Combine(Root, meta.StoreName.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(Root);
        if (!path.StartsWith(rootFull, StringComparison.Ordinal))
            throw BizException.Forbidden("非法文件路径");
        return path;
    }
}

/// <summary>控制器侧取当前用户（Claims 已含）。</summary>
public static class OperationUserScope
{
    public static long? CurrentUserId() => Common.Runtime.OperationUser.UserId;
}
