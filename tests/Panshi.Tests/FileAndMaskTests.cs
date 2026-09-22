using Panshi.Common.Security;
using Xunit;

namespace Panshi.Tests;

/// <summary>文件魔数嗅探（真伪/伪装/无签名/短内容/位置还原）。</summary>
public class FileSignatureTests
{
    [Fact]
    public void Png_Real_Signature_Matched()
        => Assert.Equal(FileSignature.SniffResult.Matched,
            FileSignature.Sniff(".png", [(byte)0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));

    [Fact]
    public void Png_Spoofed_As_Gif_Rejected()
        => Assert.Equal(FileSignature.SniffResult.Spoofed,
            FileSignature.Sniff(".png", "GIF89a____"u8.ToArray()));

    [Fact]
    public void Docx_Zip_Signature_Matched()
        => Assert.Equal(FileSignature.SniffResult.Matched,
            FileSignature.Sniff(".docx", [0x50, 0x4B, 0x03, 0x04, 1, 2, 3, 4]));

    [Fact]
    public void Txt_NoSignature_Skipped()
        => Assert.Equal(FileSignature.SniffResult.Skipped, FileSignature.Sniff(".txt", "hi"u8.ToArray()));

    [Fact]
    public void TooShort_Rejected()
        => Assert.Equal(FileSignature.SniffResult.TooShort, FileSignature.Sniff(".png", [0x89, 0x50]));

    [Fact]
    public void UnknownExtension_Treated_As_Spoofed()
        => Assert.Equal(FileSignature.SniffResult.Spoofed, FileSignature.Sniff(".exe", new byte[16]));

    [Fact]
    public void Empty_File_Rejected_By_Validate()
        => Assert.NotNull(FileSignature.Validate(".txt", [], 0));

    [Fact]
    public void Pdf_Real_Signature_Matched()
        => Assert.Equal(FileSignature.SniffResult.Matched, FileSignature.Sniff(".pdf", "%PDF-1.7"u8.ToArray()));
}

/// <summary>敏感词根脱敏。</summary>
public class SensitiveDataTests
{
    [Fact]
    public void Masks_Nested_Json_By_Key_Root()
    {
        var json = """{"user":{"name":"a","password":"p@ss"},"list":[{"token":"t1","x":1}],"newPassword":"n"}""";
        var masked = SensitiveData.MaskJson(json);
        Assert.Contains("\"password\":\"******\"", masked);
        Assert.Contains("\"token\":\"******\"", masked);
        Assert.Contains("\"newPassword\":\"******\"", masked);
        Assert.Contains("\"name\":\"a\"", masked);
        Assert.Contains("\"x\":1", masked);
    }

    [Fact]
    public void Non_Json_Is_Truncated_Not_Crashed()
        => Assert.StartsWith("plain", SensitiveData.MaskJson("plain text payload"));

    [Theory]
    [InlineData("Password", true)]
    [InlineData("confirmPassword", true)]
    [InlineData("appSecret", true)]
    [InlineData("accessToken", true)]
    [InlineData("credential", true)]
    [InlineData("userName", false)]
    [InlineData("tokenType", true)]
    public void Key_Root_Judgement(string key, bool sensitive)
        => Assert.Equal(sensitive, SensitiveData.IsSensitiveKey(key));
}
