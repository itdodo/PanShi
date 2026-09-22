namespace Panshi.Model.Dtos;

/// <summary>分页查询基类（pageNum 从 1 起；排序字段反射白名单校验，无效值回退 CreateTime desc——红线 #5）。</summary>
public abstract class PagedQuery
{
    private const int MaxPageSize = 200;

    public int PageNum { get; set; } = 1;

    private int _pageSize = 20;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = Math.Clamp(value, 1, MaxPageSize);
    }

    /// <summary>排序字段（前端驼峰，如 createTime）；服务层白名单校验后使用。</summary>
    public string? SortField { get; set; }

    /// <summary>asc / desc（缺省 desc）</summary>
    public string? SortOrder { get; set; }

    public int Offset => (Math.Max(PageNum, 1) - 1) * PageSize;

    /// <summary>
    /// 解析排序列名：sortField 必须命中白名单，否则回退 CreateTime；
    /// ⚠️ 返回物理列名（下划线），拼接进 OrderBy 前已过白名单 = 防注入。
    /// </summary>
    public (string Column, bool Desc) ResolveSort(IReadOnlyDictionary<string, string> whitelist)
    {
        var fallback = ("create_time", true);
        if (string.IsNullOrWhiteSpace(SortField)) return fallback;
        if (!whitelist.TryGetValue(SortField.Trim(), out var column) || string.IsNullOrEmpty(column)) return fallback;
        var desc = !string.Equals(SortOrder, "asc", StringComparison.OrdinalIgnoreCase);
        return (column, desc);
    }
}
