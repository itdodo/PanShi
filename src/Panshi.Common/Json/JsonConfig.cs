using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Panshi.Common.Json;

/// <summary>
/// 雪花 long → JSON 字符串（红线：前端 id 一律 string）。
/// 读回时兼容数字与字符串（配合全局 NumberHandling.AllowReadingFromString）。
/// </summary>
public sealed class LongToStringConverter : JsonConverter<long>
{
    public override long Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.String => long.Parse(reader.GetString()!),
            JsonTokenType.Number => reader.GetInt64(),
            _ => throw new JsonException("long 字段需要数字或字符串")
        };

    public override void Write(Utf8JsonWriter writer, long value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}

/// <summary>可空 long 的同上版本。</summary>
public sealed class NullableLongToStringConverter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => string.IsNullOrEmpty(reader.GetString()) ? null : long.Parse(reader.GetString()!),
            JsonTokenType.Number => reader.GetInt64(),
            _ => throw new JsonException("long? 字段需要数字、字符串或 null")
        };

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue();
        else writer.WriteStringValue(value.Value.ToString());
    }
}

/// <summary>全局 JSON 约定。</summary>
public static class JsonConfig
{
    /// <summary>
    /// 中文字面量输出、HTML 敏感字符照旧转义。
    /// 默认编码器会把所有非 ASCII 变成 \uXXXX——响应体与操作日志里的 JSON 文本全都变成乱码形态；
    /// 而 UnsafeRelaxedJsonEscaping 会连 &lt; &gt; &amp; ' 一起放开（操作日志存的是用户提交的任意文本，不拿这个换可读性）。
    /// ⚠️ 必须声明在 Options 之前：静态字段按声明顺序初始化，反了就会被赋成 null（等于没设）。
    /// </summary>
    public static readonly JavaScriptEncoder CjkFriendlyEncoder = JavaScriptEncoder.Create(
        UnicodeRanges.BasicLatin,
        UnicodeRanges.GeneralPunctuation,
        UnicodeRanges.CjkSymbolsandPunctuation,
        UnicodeRanges.CjkUnifiedIdeographs,
        UnicodeRanges.HalfwidthandFullwidthForms);

    /// <summary>服务端内部使用的标准输出选项（落库 JSON/日志等同契约层行为）。</summary>
    public static JsonSerializerOptions Options { get; } = Create(new JsonSerializerOptions());

    public static JsonSerializerOptions Create(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.Encoder = CjkFriendlyEncoder;
        options.Converters.Add(new LongToStringConverter());
        options.Converters.Add(new NullableLongToStringConverter());
        return options;
    }
}
