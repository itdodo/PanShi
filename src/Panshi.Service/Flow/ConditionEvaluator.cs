using System.Globalization;
using System.Text.Json;
using Panshi.Model.Dtos;
using Panshi.Model.Enums;

namespace Panshi.Service.Flow;

/// <summary>
/// 条件求值器（纯函数、解释执行，禁动态编译，蓝图§5.7）：
/// 变量缺失=不命中；未知操作符=不命中；数字/字符串宽容比较。
/// </summary>
public static class ConditionEvaluator
{
    /// <summary>分支求值：conditions 全部为真（AND）才命中。</summary>
    public static bool Match(FlowBranchDto branch, IReadOnlyDictionary<string, object?> variables)
        => branch.Conditions.Count > 0 && branch.Conditions.All(c => Match(c, variables));

    public static bool Match(FlowConditionDto cond, IReadOnlyDictionary<string, object?> variables)
    {
        if (!variables.TryGetValue(cond.Variable, out var raw) || raw is null) return false;

        switch (cond.Op)
        {
            case FlowConstants.Op.Lt: return Compare(raw, cond.Value) is < 0;
            case FlowConstants.Op.Le: return Compare(raw, cond.Value) is <= 0;
            case FlowConstants.Op.Gt: return Compare(raw, cond.Value) is > 0;
            case FlowConstants.Op.Ge: return Compare(raw, cond.Value) is >= 0;
            case FlowConstants.Op.Eq: return Equal(raw, cond.Value);
            case FlowConstants.Op.Ne: return !Equal(raw, cond.Value);
            case FlowConstants.Op.In: return InList(raw, cond.Value);
            case FlowConstants.Op.Contains: return ToStr(raw).Contains(ToStr(cond.Value), StringComparison.Ordinal);
            default: return false; // 未知操作符=不命中
        }
    }

    /// <summary>选分支：priority 升序首个真分支；全不中走 defaultNext（可空=直接结束）。</summary>
    public static (string? Next, FlowBranchDto? Hit) Pick(FlowNodeDto node,
        IReadOnlyDictionary<string, object?> variables)
    {
        foreach (var b in (node.Branches ?? []).OrderBy(b => b.Priority))
            if (Match(b, variables)) return (b.Next, b);
        return (node.DefaultNext, null);
    }

    private static int? Compare(object raw, object? expected)
    {
        if (TryNum(raw, out var a) && TryNum(expected, out var b)) return a.CompareTo(b);
        // 非数字不可比=不命中（保守）
        return null;
    }

    private static bool Equal(object raw, object? expected)
    {
        if (TryNum(raw, out var a) && TryNum(expected, out var b)) return a == b;
        return string.Equals(ToStr(raw), ToStr(expected), StringComparison.Ordinal);
    }

    private static bool InList(object raw, object? expected)
    {
        var items = expected switch
        {
            JsonElement { ValueKind: JsonValueKind.Array } je => je.EnumerateArray().Select(x => ToStr(x)).ToList(),
            System.Collections.IEnumerable e and not string => e.Cast<object?>().Select(x => ToStr(x)).ToList(),
            _ => null
        };
        if (items is null) return false;
        if (TryNum(raw, out var n))
            return items.Any(i => TryNum(i, out var m) && m == n);
        return items.Contains(ToStr(raw), StringComparer.Ordinal);
    }

    private static bool TryNum(object? v, out decimal d)
    {
        d = 0;
        switch (v)
        {
            case null: return false;
            case JsonElement je:
                if (je.ValueKind == JsonValueKind.Number) return je.TryGetDecimal(out d);
                if (je.ValueKind != JsonValueKind.String) return false;
                return decimal.TryParse(je.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d);
            case string s: return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d);
            case bool b: d = b ? 1 : 0; return true;
            case IConvertible c:
                try { d = c.ToDecimal(CultureInfo.InvariantCulture); return true; }
                catch { return false; }
            default: return false;
        }
    }

    private static string ToStr(object? v) => v switch
    {
        null => "",
        JsonElement je => je.ValueKind == JsonValueKind.String ? je.GetString() ?? "" : je.GetRawText(),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? ""
    };
}

/// <summary>DSL 结构校验（保存/提交双时机执行，非法即拒）。</summary>
public static class FlowGraphValidator
{
    public static (bool Ok, string Msg) Validate(string nodeJson)
    {
        FlowGraph graph;
        try
        {
            graph = Parse(nodeJson);
        }
        catch (Exception ex)
        {
            return (false, $"NodeJson 解析失败：{ex.Message}");
        }

        if (graph.Nodes.Count == 0) return (false, "节点清单为空");
        var codes = graph.Nodes.Select(n => n.Code).ToList();
        if (codes.Distinct().Count() != codes.Count) return (false, "节点 code 重复");
        var codeSet = codes.ToHashSet();
        if (graph.Start is null) return (false, "缺少 entry 入口节点");

        foreach (var n in graph.Nodes)
        {
            var refs = new List<string?> { n.Next, n.DefaultNext };
            refs.AddRange((n.Branches ?? []).Select(b => b.Next));
            foreach (var r in refs.Where(r => !string.IsNullOrEmpty(r)))
                if (!codeSet.Contains(r!)) return (false, $"节点 {n.Code} 指向不存在的节点 {r}");

            switch (n.Type)
            {
                case FlowConstants.NodeType.Start:
                    if (string.IsNullOrEmpty(n.Next)) return (false, "start 节点必须有 next");
                    break;
                case FlowConstants.NodeType.Approval:
                    if (string.IsNullOrEmpty(n.Next)) return (false, $"审批节点 {n.Code} 缺少 next");
                    if ((n.Approvers ?? []).Count == 0) return (false, $"审批节点 {n.Code} 未配置审批人规则");
                    if (!string.IsNullOrEmpty(n.Mode) && n.Mode is not (FlowConstants.NodeMode.OrSign or FlowConstants.NodeMode.Countersign or FlowConstants.NodeMode.Sequential))
                        return (false, $"审批节点 {n.Code} mode 非法：{n.Mode}");
                    break;
                case FlowConstants.NodeType.Condition:
                    if ((n.Branches ?? []).Count == 0 && string.IsNullOrEmpty(n.DefaultNext))
                        return (false, $"条件节点 {n.Code} 既无分支也无默认走向");
                    break;
                case FlowConstants.NodeType.Cc:
                    if (string.IsNullOrEmpty(n.Next)) return (false, $"抄送节点 {n.Code} 缺少 next");
                    break;
                case FlowConstants.NodeType.End:
                    break;
                default:
                    return (false, $"未知节点类型：{n.Type}");
            }
        }

        // 可达性（从 entry 走一遍，孤立节点报错防呆）
        var reachable = new HashSet<string>();
        var queue = new Queue<string>([graph.Entry]);
        while (queue.Count > 0)
        {
            var code = queue.Dequeue();
            if (!reachable.Add(code)) continue;
            var node = graph.Find(code);
            if (node is null) continue;
            var nexts = new List<string?> { node.Next, node.DefaultNext };
            nexts.AddRange((node.Branches ?? []).Select(b => b.Next));
            foreach (var nx in nexts.Where(x => !string.IsNullOrEmpty(x))) queue.Enqueue(nx!);
        }

        var orphan = codes.Except(reachable).ToList();
        if (orphan.Count > 0) return (false, $"存在不可达孤立节点：{string.Join(",", orphan)}");
        if (!reachable.Contains("end") && !codes.Contains(FlowConstants.NodeType.End))
            return (false, "缺少 end 节点");
        return (true, "ok");
    }

    public static FlowGraph Parse(string nodeJson) =>
        JsonSerializer.Deserialize<FlowGraph>(nodeJson, Common.Json.JsonConfig.Options) ?? new FlowGraph();
}
