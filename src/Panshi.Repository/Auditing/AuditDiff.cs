using System.Text.Json;
using Panshi.Common.Security;

namespace Panshi.Repository.Auditing;

/// <summary>字段级变更条目（sys_change_log.Changes JSON 元素）。</summary>
public sealed class ChangeItem
{
    public string Field { get; set; } = "";

    public string Label { get; set; } = "";

    public string? Before { get; set; }

    public string? After { get; set; }
}

/// <summary>
/// 反射 diff 新旧实体：排除审计列与乐观锁列；敏感词根字段值以掩码替换；
/// 无差异返回空集合（无差异不落审计由调用方保证）。
/// </summary>
public static class AuditDiff
{
    private static readonly HashSet<string> Excluded =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Id", "CreateTime", "CreateBy", "UpdateTime", "UpdateBy", "IsDeleted", "Version"
        };

    public static List<ChangeItem> Diff<T>(T oldObj, T currentObj)
    {
        var list = new List<ChangeItem>();
        if (ReferenceEquals(oldObj, currentObj)) return list;

        foreach (var prop in typeof(T).GetProperties())
        {
            if (!prop.CanRead || Excluded.Contains(prop.Name)) continue;
            var before = Normalize(prop.GetValue(oldObj));
            var after = Normalize(prop.GetValue(currentObj));
            if (string.Equals(before, after, StringComparison.Ordinal)) continue;

            var sensitive = SensitiveData.IsSensitiveKey(prop.Name);
            list.Add(new ChangeItem
            {
                Field = ToCamel(prop.Name),
                Label = prop.Name,
                Before = sensitive ? SensitiveData.Mask : before,
                After = sensitive ? SensitiveData.Mask : after
            });
        }

        return list;
    }

    public static string ToJson(List<ChangeItem> changes)
        => JsonSerializer.Serialize(changes, SensitiveData.JsonOptions);

    private static string? Normalize(object? value) => value switch
    {
        null => null,
        string s => s,
        DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss"),
        bool b => b ? "1" : "0",
        Enum e => e.ToString("d"),
        _ => JsonSerializer.Serialize(value, SensitiveData.JsonOptions)
    };

    private static string ToCamel(string name) =>
        name.Length > 1 && char.IsUpper(name[0]) ? char.ToLowerInvariant(name[0]) + name[1..] : name;
}
