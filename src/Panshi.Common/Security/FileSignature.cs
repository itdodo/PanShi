namespace Panshi.Common.Security;

/// <summary>
/// 上传文件 magic bytes 内容嗅探（安全清单 #4）：
/// 有签名类型必须与扩展名匹配；无签名类型（纯文本类）跳过；短内容（&lt;4 字节）拒绝。
/// </summary>
public static class FileSignature
{
    /// <summary>嗅探结论。</summary>
    public enum SniffResult
    {
        /// <summary>扩展名属于无签名类型，跳过嗅探</summary>
        Skipped,

        /// <summary>签名与扩展名匹配</summary>
        Matched,

        /// <summary>内容太短，无法嗅探 → 拒绝</summary>
        TooShort,

        /// <summary>伪装（签名与扩展名不符或未知）→ 拒绝</summary>
        Spoofed
    }

    /// <summary>无 magic bytes 的纯文本类扩展名（白名单内直接放行）。</summary>
    public static readonly HashSet<string> NoSignatureExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv", ".md", ".json", ".xml", ".log" };

    private static readonly Dictionary<string, byte[][]> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = [[0x25, 0x50, 0x44, 0x46]], // %PDF
        [".png"] = [[0x89, 0x50, 0x4E, 0x47]],
        [".jpg"] = [[0xFF, 0xD8, 0xFF]],
        [".jpeg"] = [[0xFF, 0xD8, 0xFF]],
        [".gif"] = [[0x47, 0x49, 0x46, 0x38]], // GIF8
        [".bmp"] = [[0x42, 0x4D]],
        [".zip"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".docx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".xlsx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".pptx"] = [[0x50, 0x4B, 0x03, 0x04]],
        [".rar"] = [[0x52, 0x61, 0x72, 0x21]], // Rar!
        [".7z"] = [[0x37, 0x7A, 0xBC, 0xAF]],
        [".xls"] = [[0xD0, 0xCF, 0x11, 0xE0]], // OLE
        [".doc"] = [[0xD0, 0xCF, 0x11, 0xE0]],
        [".ppt"] = [[0xD0, 0xCF, 0x11, 0xE0]],
    };

    /// <summary>嗅探文件头（head 为文件前 ≤64 字节；webp 特判 RIFF....WEBP）。</summary>
    public static SniffResult Sniff(string extension, ReadOnlySpan<byte> head)
    {
        if (NoSignatureExtensions.Contains(extension)) return SniffResult.Skipped;
        if (!Signatures.TryGetValue(extension, out var candidates)) return SniffResult.Spoofed;

        // RIFF 容器（webp 不在默认白名单，预留）
        if (head.Length < 4) return SniffResult.TooShort;
        foreach (var sig in candidates)
        {
            if (head.Length >= sig.Length && head[..sig.Length].SequenceEqual(sig))
                return SniffResult.Matched;
        }

        // 短于该类型签名长度 → 视为短内容拒绝（如 2 字节的 .png）
        var minSig = candidates.Min(c => c.Length);
        return head.Length < minSig ? SniffResult.TooShort : SniffResult.Spoofed;
    }

    /// <summary>校验（控制器入口）：返回错误消息或 null。</summary>
    public static string? Validate(string extension, ReadOnlySpan<byte> head, long totalSize)
    {
        if (totalSize < 1) return "文件内容为空";
        return Sniff(extension, head) switch
        {
            SniffResult.Skipped => null,
            SniffResult.Matched => null,
            SniffResult.TooShort => "文件内容过短，疑似伪装",
            _ => "文件内容与扩展名不符，已拒绝"
        };
    }
}
