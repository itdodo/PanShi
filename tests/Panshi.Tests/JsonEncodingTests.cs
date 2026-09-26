using System.Text.Json;
using Panshi.Common.Json;
using Panshi.Common.Security;
using Xunit;

namespace Panshi.Tests;

/// <summary>全局 JSON 编码约定：中文可读，但 HTML 敏感字符照旧转义。</summary>
public class JsonEncodingTests
{
    private static string Serialize(object value) => JsonSerializer.Serialize(value, JsonConfig.Options);

    private sealed record Row(string Name, string Html);

    [Theory]
    [InlineData("登录验证码开关")]
    [InlineData("「磐石」，2026（年度）！")]
    [InlineData("全角ＡＢＣ１２３")]
    public void Cjk_And_Punctuation_Serialize_Literally(string text)
    {
        var json = Serialize(new Row(text, "x"));
        Assert.Contains(text, json);
        Assert.DoesNotContain("\\u", json);
    }

    [Fact]
    public void Html_Sensitive_Chars_Still_Escaped()
    {
        var json = Serialize(new Row("n", "<script>alert('1')</script>&"));
        Assert.DoesNotContain("<script>", json);
        Assert.DoesNotContain("alert('1')", json);
        Assert.Contains("\\u003C", json);
        Assert.Contains("\\u0026", json);
    }

    [Fact]
    public void Snowflake_Long_Still_String_And_Keys_CamelCased()
    {
        var json = Serialize(new Dictionary<string, long> { ["DictTypeId"] = 852906110251077 });
        Assert.Equal("""{"dictTypeId":"852906110251077"}""", json);
    }

    /// <summary>回归：操作日志走「序列化→脱敏→再序列化」，第二步曾把中文又转义回 \uXXXX。</summary>
    [Fact]
    public void MaskJson_Keeps_Cjk_Literal_And_Still_Masks_Secrets()
    {
        var masked = SensitiveData.MaskJson("""{"dto":{"configName":"登录验证码开关","password":"Abcd1234"}}""");
        Assert.Contains("登录验证码开关", masked);
        Assert.DoesNotContain("\\u", masked);
        Assert.Contains(SensitiveData.Mask, masked);
        Assert.DoesNotContain("Abcd1234", masked);
    }
}
