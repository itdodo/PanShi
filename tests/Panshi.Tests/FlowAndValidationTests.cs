using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Panshi.Model.Validation;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Enums;
using Panshi.Service.Flow;
using Xunit;

namespace Panshi.Tests;

/// <summary>条件求值器（操作符/类型宽容/缺失变量/未知操作符——蓝图§5.7 解释执行基准）。</summary>
public class ConditionEvaluatorTests
{
    private static readonly Dictionary<string, object?> Vars = new()
    {
        ["amount"] = 5000m,
        ["amountStr"] = "5000",
        ["dept"] = "fin",
        ["flag"] = true,
        ["list"] = "a,b"
    };

    private static FlowConditionDto C(string v, string op, object? val) =>
        new() { Variable = v, Op = op, Value = val };

    [Theory]
    [InlineData("amount", FlowConstants.Op.Lt, 10000, true)]
    [InlineData("amount", FlowConstants.Op.Lt, 5000, false)]
    [InlineData("amount", FlowConstants.Op.Le, 5000, true)]
    [InlineData("amount", FlowConstants.Op.Gt, 4999, true)]
    [InlineData("amount", FlowConstants.Op.Eq, 5000, true)]
    [InlineData("amount", FlowConstants.Op.Ne, 5000, false)]
    [InlineData("dept", FlowConstants.Op.Eq, "fin", true)]
    [InlineData("dept", FlowConstants.Op.Contains, "in", true)]
    public void Operators(string variable, string op, object value, bool expected)
        => Assert.Equal(expected, ConditionEvaluator.Match(C(variable, op, value), Vars));

    [Fact]
    public void String_Number_Tolerant_Compare()
        => Assert.True(ConditionEvaluator.Match(C("amountStr", FlowConstants.Op.Lt, 10000), Vars));

    [Fact]
    public void JsonElement_Numbers_Work()
    {
        using var doc = JsonDocument.Parse("{\"amount\": 60}");
        var vars = new Dictionary<string, object?> { ["amount"] = doc.RootElement.GetProperty("amount") };
        Assert.True(ConditionEvaluator.Match(C("amount", FlowConstants.Op.Gt, 50), vars));
    }

    [Fact]
    public void Missing_Variable_Not_Hit()
        => Assert.False(ConditionEvaluator.Match(C("nonexistent", FlowConstants.Op.Eq, 1), Vars));

    [Fact]
    public void Unknown_Operator_Not_Hit()
        => Assert.False(ConditionEvaluator.Match(C("amount", "between", 100), Vars));

    [Fact]
    public void In_Operator_Matches_Number_And_String()
    {
        Assert.True(ConditionEvaluator.Match(C("dept", FlowConstants.Op.In, new[] { "hr", "fin" }), Vars));
        Assert.True(ConditionEvaluator.Match(C("amount", FlowConstants.Op.In, new[] { 100, 5000 }), Vars));
        Assert.False(ConditionEvaluator.Match(C("dept", FlowConstants.Op.In, new[] { "hr" }), Vars));
    }

    [Fact]
    public void Pick_First_True_By_Priority_Then_Default()
    {
        var node = new FlowNodeDto
        {
            Code = "c1", Type = FlowConstants.NodeType.Condition, DefaultNext = "fallback",
            Branches =
            [
                new FlowBranchDto { Name = "高", Priority = 2, Next = "n_high", Conditions = [C("amount", FlowConstants.Op.Gt, 100)] },
                new FlowBranchDto { Name = "低", Priority = 1, Next = "n_low", Conditions = [C("amount", FlowConstants.Op.Lt, 100)] }
            ]
        };
        // 仅「高」命中（priority 升序求值，首个真分支胜出）
        var (next, hit) = ConditionEvaluator.Pick(node, Vars);
        Assert.Equal("n_high", next);
        Assert.Equal("高", hit!.Name);

        node.Branches[0].Conditions = [C("amount", FlowConstants.Op.Gt, 99999)];
        var (dNext, dHit) = ConditionEvaluator.Pick(node, Vars);
        Assert.Equal("fallback", dNext);
        Assert.Null(dHit);
    }

    [Fact]
    public void Empty_Branch_Conditions_Not_Hit()
    {
        var node = new FlowNodeDto
        {
            Code = "c", Type = FlowConstants.NodeType.Condition, DefaultNext = "d",
            Branches = [new FlowBranchDto { Name = "空", Next = "x", Conditions = [] }]
        };
        Assert.Equal("d", ConditionEvaluator.Pick(node, Vars).Next);
    }
}

/// <summary>选填校验特性（红线 #15/#16：空串放行 + 严格国内手机号）。</summary>
public class OptionalValidationTests
{
    private static bool Valid(object? value, ValidationAttribute attr)
        => attr.GetValidationResult(value, new System.ComponentModel.DataAnnotations.ValidationContext(new object())) is null;

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("13800138000", true)]
    [InlineData("12345678901", false)] // 第二位非法
    [InlineData("1380013800", false)] // 10 位（内置 Phone 会放行，自定义必须拦）
    [InlineData("138001380001", false)] // 12 位
    [InlineData("0755-12345678", false)] // 固话
    public void Phone(object? value, bool expected) => Assert.Equal(expected, Valid(value, new OptionalPhoneAttribute()));

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("a@b.co", true)]
    [InlineData("plain", false)]
    [InlineData("a@@b.co", false)]
    public void Email(object? value, bool expected) => Assert.Equal(expected, Valid(value, new OptionalEmailAddressAttribute()));
}

/// <summary>分页排序白名单（红线 #5：无效回退 CreateTime 而非 Id）。</summary>
public class PagedQuerySortTests
{
    private sealed class Q : PagedQuery;

    private static readonly Dictionary<string, string> Whitelist = new()
    {
        ["createTime"] = "create_time", ["userName"] = "user_name"
    };

    [Fact]
    public void Valid_Field_Hits_Column()
    {
        var q = new Q { SortField = "userName", SortOrder = "asc" };
        var (col, desc) = q.ResolveSort(Whitelist);
        Assert.Equal("user_name", col);
        Assert.False(desc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("id")] // 白名单不含 id → 回退 create_time（红线 #5）
    [InlineData("1;drop table")]
    public void Invalid_Falls_Back_To_CreateTime(string? field)
    {
        var q = new Q { SortField = field };
        var (col, desc) = q.ResolveSort(Whitelist);
        Assert.Equal("create_time", col);
        Assert.True(desc);
    }

    [Fact]
    public void PageSize_Clamped()
    {
        var q = new Q { PageSize = 99999 };
        Assert.Equal(200, q.PageSize);
        q.PageSize = -5;
        Assert.Equal(1, q.PageSize);
    }
}

/// <summary>FlowGraph DSL 校验器。</summary>
public class FlowGraphValidatorTests
{
    [Fact]
    public void Valid_Graph_Passes()
    {
        var json = """
        {"nodes":[
          {"code":"start","type":"start","next":"n1"},
          {"code":"n1","type":"approval","name":"审","mode":"orSign","next":"end","approvers":[{"type":"user","userIds":["1"]}]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (ok, _) = FlowGraphValidator.Validate(json);
        Assert.True(ok);
    }

    [Fact]
    public void Dangling_Reference_Fails()
    {
        var json = """
        {"nodes":[
          {"code":"start","type":"start","next":"missing"},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (ok, msg) = FlowGraphValidator.Validate(json);
        Assert.False(ok);
        Assert.Contains("missing", msg);
    }

    [Fact]
    public void Approval_Without_Next_Fails()
    {
        var json = """
        {"nodes":[
          {"code":"start","type":"start","next":"n1"},
          {"code":"n1","type":"approval","approvers":[{"type":"user","userIds":["1"]}]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (ok, _) = FlowGraphValidator.Validate(json);
        Assert.False(ok);
    }

    [Fact]
    public void Orphan_Node_Fails()
    {
        var json = """
        {"nodes":[
          {"code":"start","type":"start","next":"end"},
          {"code":"orphan","type":"approval","next":"end","approvers":[{"type":"user","userIds":["1"]}]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (ok, msg) = FlowGraphValidator.Validate(json);
        Assert.False(ok);
        Assert.Contains("orphan", msg);
    }
}
