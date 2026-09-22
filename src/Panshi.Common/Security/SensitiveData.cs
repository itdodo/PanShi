using System.Text.Json;
using System.Text.Json.Nodes;

namespace Panshi.Common.Security;

/// <summary>
/// 敏感数据脱敏（安全清单 #6）：操作日志参数与字段级 diff 按词根
/// password / secret / token / credential 命中键名即将值替换为 "******"。
/// </summary>
public static class SensitiveData
{
    public const string Mask = "******";

    private static readonly string[] Roots = ["password", "secret", "token", "credential"];

    public static bool IsSensitiveKey(string? key) =>
        key is not null && Roots.Any(r => key.Contains(r, StringComparison.OrdinalIgnoreCase));

    /// <summary>递归脱敏 JSON 文本（非 JSON 原样返回；解析失败原样返回并截断）。</summary>
    public static string MaskJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "";
        try
        {
            var node = JsonNode.Parse(json);
            MaskNode(node);
            return node?.ToJsonString(JsonOptions) ?? "";
        }
        catch (JsonException)
        {
            return Truncate(json);
        }
    }

    private static void MaskNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj.ToList())
                {
                    if (IsSensitiveKey(key)) obj[key] = JsonValue.Create(Mask);
                    else MaskNode(value);
                }

                break;
            case JsonArray arr:
                foreach (var item in arr) MaskNode(item);
                break;
        }
    }

    /// <summary>日志参数裁剪（避免超大请求体落库）。</summary>
    public static string Truncate(string s, int max = 4000) =>
        s.Length <= max ? s : s[..max] + "...(truncated)";

    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };
}
