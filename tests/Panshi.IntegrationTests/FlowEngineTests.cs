using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Sys;
using Xunit;

namespace Panshi.IntegrationTests;

/// <summary>审批流九大语义（真实表、服务层直测；每用例独立流程码防串扰）。</summary>
[Collection("pg")]
public class FlowEngineTests(PgFixture fx) : PgTestBase(fx)
{
    private static string Nid() => SnowflakeId.NextId().ToString();

    private async Task<(SysFlowDefinition def, string code)> CreateAndEnableAsync(string nodeJson)
    {
        var defs = await Db.Queryable<SysFlowDefinition>().ToListAsync();
        var max = defs.Select(d => int.TryParse(d.FlowCode, out var v) ? v : 100).DefaultIfEmpty(100).Max();
        var code = (max + 1).ToString();
        var def = new SysFlowDefinition
        {
            Id = SnowflakeId.NextId(), FlowCode = code, FlowName = "测试" + code, NodeJson = nodeJson,
            FlowVersion = 1, Status = 1, CreateTime = DateTime.Now
        };
        await Db.Insertable(def).ExecuteCommandAsync();
        return (def, code);
    }

    private async Task BindAsync(string table, string code)
    {
        var existing = await Db.Queryable<SysFlowBinding>().FirstAsync(b => b.BusinessTable == table);
        if (existing is not null) await Db.Deleteable<SysFlowBinding>().Where(b => b.Id == existing.Id).ExecuteCommandAsync();
        await Db.Insertable(new SysFlowBinding
        {
            Id = SnowflakeId.NextId(), BusinessTable = table, FlowCode = code, Status = 1, CreateTime = DateTime.Now
        }).ExecuteCommandAsync();
    }

    private async Task<BizExpense> NewExpenseAsync(decimal amount, long uid, string uname, long? dept)
    {
        var e = new BizExpense
        {
            Id = SnowflakeId.NextId(), DocNo = "BX" + Nid(), OwnerUserId = uid, OwnerUserName = uname,
            DeptId = dept, Amount = amount, Reason = "集成测试", Status = BizDocStatus.Draft, CreateTime = DateTime.Now
        };
        await Db.Insertable(e).ExecuteCommandAsync();
        return e;
    }

    private string OrSignGraph(string approverId) =>
        $$"""
        {"nodes":[
          {"code":"start","type":"start","next":"n1"},
          {"code":"n1","type":"approval","name":"审批","mode":"orSign","next":"end","approvers":[{"type":"user","userIds":["{{approverId}}"]}]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;

    [Fact]
    public async Task Submit_Without_Binding_Returns_MinusOne_DirectPass()
    {
        var engine = Fx.Engine();
        var exp = await NewExpenseAsync(1, 1, "admin", 2);
        var unbindTable = "biz_expense_unbound_" + Nid();
        var id = await engine.SubmitAsync(new FlowSubmitDto
        {
            BusinessTable = unbindTable, BusinessId = exp.Id, Variables = new()
        }, 1, "admin");
        Assert.Equal(-1, id);
    }

    [Fact]
    public async Task Empty_Approvers_AutoPass_Ends_And_Callback()
    {
        // 审批人解析为空（角色无任何成员）→ 自动通过直达 end
        var graph = """
        {"nodes":[
          {"code":"start","type":"start","next":"n1"},
          {"code":"n1","type":"approval","name":"审批","mode":"orSign","next":"end","approvers":[{"type":"role","roleCodes":["no_such_role_xyz"]}]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (def, code) = await CreateAndEnableAsync(graph);
        await BindAsync("biz_expense", code);
        var engine = Fx.Engine();
        using var _ = As(1, "admin", 2);
        var exp = await NewExpenseAsync(10, 1, "admin", 2);
        var instanceId = await engine.SubmitAsync(new FlowSubmitDto
        {
            BusinessTable = "biz_expense", BusinessId = exp.Id, Variables = new Dictionary<string, object> { ["amount"] = 10 }
        }, 1, "admin");
        Assert.True(instanceId > 0);

        var inst = await Db.Queryable<SysFlowInstance>().InSingleAsync(instanceId);
        Assert.Equal(FlowInstanceStatus.Approved, inst.Status);
        Assert.Equal(BizDocStatus.Approved, (await Db.Queryable<BizExpense>().InSingleAsync(exp.Id)).Status);
        Assert.True(await Db.Queryable<SysFlowRecord>().AnyAsync(r => r.InstanceId == instanceId && r.Action == "auto"));
    }

    [Fact]
    public async Task Approver_Equals_Submitter_AutoPassed()
    {
        var (def, code) = await CreateAndEnableAsync(OrSignGraph("1")); // admin=1 自己审自己
        await BindAsync("biz_expense", code);
        using var _ = As(1, "admin", 2);
        var exp = await NewExpenseAsync(20, 1, "admin", 2);
        var instanceId = await Fx.Engine().SubmitAsync(new FlowSubmitDto
        {
            BusinessTable = "biz_expense", BusinessId = exp.Id, Variables = new Dictionary<string, object> { ["amount"] = 20 }
        }, 1, "admin");

        var tasks = await Db.Queryable<SysFlowTask>().Where(t => t.InstanceId == instanceId).ToListAsync();
        Assert.Single(tasks);
        Assert.Equal(FlowTaskStatus.AutoPassed, tasks[0].Status);
        Assert.Equal(FlowInstanceStatus.Approved, (await Db.Queryable<SysFlowInstance>().InSingleAsync(instanceId)).Status);
    }

    [Fact]
    public async Task SubmitterChoice_Needs_ChoiceUserIds_Otherwise_AutoPasses()
    {
        // 自选节点：带人=真生成待办；不带人=解析为空→自动通过。
        // 漏传不会报错，只会把人工审批静默跳过，所以前端提交前的必选弹窗要有这条语义兜着。
        var graph = """
        {"nodes":[
          {"code":"start","type":"start","next":"n1"},
          {"code":"n1","type":"approval","name":"自选","mode":"orSign","next":"end","approvers":[{"type":"submitterChoice"}]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (_, code) = await CreateAndEnableAsync(graph);
        await BindAsync("biz_expense", code);
        using var _ = As(1, "admin", 2);
        var chosen = await EnsureUserAsync("t_flow_choice", 3);

        var instId = await Fx.Engine().SubmitAsync(new FlowSubmitDto
        {
            BusinessTable = "biz_expense", BusinessId = (await NewExpenseAsync(30, 1, "admin", 2)).Id,
            Variables = new Dictionary<string, object> { ["amount"] = 30 },
            ChoiceUserIds = [chosen.Id.ToString()]
        }, 1, "admin");
        var task = (await Db.Queryable<SysFlowTask>().Where(t => t.InstanceId == instId).ToListAsync()).Single();
        Assert.Equal(chosen.Id, task.ApproverUserId);
        Assert.Equal(FlowTaskStatus.Pending, task.Status);
        Assert.Equal(FlowInstanceStatus.Running, (await Db.Queryable<SysFlowInstance>().InSingleAsync(instId)).Status);

        var noChoice = await NewExpenseAsync(30, 1, "admin", 2);
        var autoId = await Fx.Engine().SubmitAsync(new FlowSubmitDto
        {
            BusinessTable = "biz_expense", BusinessId = noChoice.Id,
            Variables = new Dictionary<string, object> { ["amount"] = 30 }
        }, 1, "admin");
        Assert.Equal(FlowInstanceStatus.Approved, (await Db.Queryable<SysFlowInstance>().InSingleAsync(autoId)).Status);
        Assert.True(await Db.Queryable<SysFlowRecord>().AnyAsync(r => r.InstanceId == autoId && r.Action == "auto"));
    }

    [Fact]
    public async Task Pending_Then_Approve_Completes()
    {
        var wang = await EnsureUserAsync("t_flow_wang", 3); // 审批人=王五，发起人=admin
        var (def, code) = await CreateAndEnableAsync(OrSignGraph(wang.Id.ToString()));
        await BindAsync("biz_expense", code);
        long instanceId;
        using (As(1, "admin", 2))
        {
            var exp = await NewExpenseAsync(30, 1, "admin", 2);
            instanceId = await Fx.Engine().SubmitAsync(new FlowSubmitDto
            {
                BusinessTable = "biz_expense", BusinessId = exp.Id, Variables = new Dictionary<string, object> { ["amount"] = 30 }
            }, 1, "admin");
        }

        var task = await Db.Queryable<SysFlowTask>().FirstAsync(t => t.InstanceId == instanceId);
        Assert.Equal(FlowTaskStatus.Pending, task.Status);
        Assert.Contains(Fx.Notify.Notices, n => n.Uid == wang.Id); // 任务生成通知

        using (As(wang.Id, "t_flow_wang", 3))
            await Fx.Engine().HandleAsync(task.Id, new FlowActDto { Action = "approve", Comment = "同意" }, wang.Id, "王五");

        Assert.Equal(FlowInstanceStatus.Approved, (await Db.Queryable<SysFlowInstance>().InSingleAsync(instanceId)).Status);
    }

    [Fact]
    public async Task Reject_Is_Final_And_Others_Invalidated()
    {
        var u1 = await EnsureUserAsync("t_flow_r1", 3);
        var u2 = await EnsureUserAsync("t_flow_r2", 4);
        var graph = $$"""
        {"nodes":[
          {"code":"start","type":"start","next":"n1"},
          {"code":"n1","type":"approval","name":"会签","mode":"countersign","next":"end","approvers":[{"type":"user","userIds":["{{u1.Id}}","{{u2.Id}}"]}]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (_, code) = await CreateAndEnableAsync(graph);
        await BindAsync("biz_expense", code);
        var exp = await NewExpenseAsync(40, 1, "admin", 2);
        long instanceId;
        using (As(1, "admin", 2))
            instanceId = await Fx.Engine().SubmitAsync(new FlowSubmitDto
            {
                BusinessTable = "biz_expense", BusinessId = exp.Id, Variables = new Dictionary<string, object> { ["amount"] = 40 }
            }, 1, "admin");

        var tasks = await Db.Queryable<SysFlowTask>().Where(t => t.InstanceId == instanceId).ToListAsync();
        Assert.Equal(2, tasks.Count(t => t.Status == FlowTaskStatus.Pending));

        using (As(u1.Id, u1.UserName, 3))
            await Fx.Engine().HandleAsync(tasks[0].Id, new FlowActDto { Action = "reject", Comment = "不同意" }, u1.Id, "r1");

        var inst = await Db.Queryable<SysFlowInstance>().InSingleAsync(instanceId);
        Assert.Equal(FlowInstanceStatus.Rejected, inst.Status);
        var rest = await Db.Queryable<SysFlowTask>().Where(t => t.InstanceId == instanceId && t.Id != tasks[0].Id).ToListAsync();
        Assert.All(rest, t => Assert.Equal(FlowTaskStatus.Invalidated, t.Status));
        Assert.Equal(BizDocStatus.Rejected, (await Db.Queryable<BizExpense>().InSingleAsync(exp.Id)).Status);
    }

    [Fact]
    public async Task Countersign_Needs_All_And_Sequential_Activates_In_Order()
    {
        var u1 = await EnsureUserAsync("t_flow_s1", 3);
        var u2 = await EnsureUserAsync("t_flow_s2", 4);
        var graph = $$"""
        {"nodes":[
          {"code":"start","type":"start","next":"n1"},
          {"code":"n1","type":"approval","name":"依次","mode":"sequential","next":"end","approvers":[{"type":"user","userIds":["{{u1.Id}}","{{u2.Id}}"]}]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (_, code) = await CreateAndEnableAsync(graph);
        await BindAsync("biz_expense", code);
        var exp = await NewExpenseAsync(50, 1, "admin", 2);
        long instanceId;
        using (As(1, "admin", 2))
            instanceId = await Fx.Engine().SubmitAsync(new FlowSubmitDto
            {
                BusinessTable = "biz_expense", BusinessId = exp.Id, Variables = new Dictionary<string, object> { ["amount"] = 50 }
            }, 1, "admin");

        var tasks = (await Db.Queryable<SysFlowTask>().Where(t => t.InstanceId == instanceId).ToListAsync())
            .OrderBy(t => t.Sequence).ToList();
        Assert.Equal(FlowTaskStatus.Pending, tasks[0].Status);
        Assert.Equal(FlowTaskStatus.Waiting, tasks[1].Status);

        using (As(tasks[0].ApproverUserId, tasks[0].ApproverName, 3))
            await Fx.Engine().HandleAsync(tasks[0].Id, new FlowActDto { Action = "approve" }, tasks[0].ApproverUserId, "s1");
        Assert.Equal(FlowTaskStatus.Pending, (await Db.Queryable<SysFlowTask>().InSingleAsync(tasks[1].Id)).Status);

        using (As(tasks[1].ApproverUserId, tasks[1].ApproverName, 4))
            await Fx.Engine().HandleAsync(tasks[1].Id, new FlowActDto { Action = "approve" }, tasks[1].ApproverUserId, "s2");
        Assert.Equal(FlowInstanceStatus.Approved, (await Db.Queryable<SysFlowInstance>().InSingleAsync(instanceId)).Status);
    }

    [Fact]
    public async Task Return_To_Start_Generates_Resubmit_And_Closes_AutoPass()
    {
        // 双审同人链：n1[B审]→n2[B审]。若 n2 无 return 记录则 B 重复审批自动通过；
        // 本用例在 n2 前制造 return 记录 → 重走后 n1/n2 的 B 不得自动通过（红线：退回后关闭重复自动通过）。
        var b = await EnsureUserAsync("t_flow_rt_b", 3);
        var graph = $$"""
        {"nodes":[
          {"code":"start","type":"start","next":"n1"},
          {"code":"n1","type":"approval","name":"初审","mode":"orSign","next":"n2","approvers":[{"type":"user","userIds":["{{b.Id}}"]}]},
          {"code":"n2","type":"approval","name":"复审","mode":"orSign","next":"end","approvers":[{"type":"user","userIds":["{{b.Id}}"]}]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (_, code) = await CreateAndEnableAsync(graph);
        await BindAsync("biz_expense", code);
        var exp = await NewExpenseAsync(60, 1, "admin", 2);
        long inst;
        using (As(1, "admin", 2))
            inst = await Fx.Engine().SubmitAsync(new FlowSubmitDto
            {
                BusinessTable = "biz_expense", BusinessId = exp.Id, Variables = new Dictionary<string, object> { ["amount"] = 60 }
            }, 1, "admin");

        // n1 B 通过 → 进 n2（B 重复但无 return → 自动通过 → 实例直接结束）——先证明「默认会 auto-pass」
        var n1Task = await Db.Queryable<SysFlowTask>().FirstAsync(t => t.InstanceId == inst && t.NodeCode == "n1");
        using (As(b.Id, b.UserName, 3))
            await Fx.Engine().HandleAsync(n1Task.Id, new FlowActDto { Action = "approve" }, b.Id, "B");
        Assert.Equal(FlowInstanceStatus.Approved, (await Db.Queryable<SysFlowInstance>().InSingleAsync(inst)).Status);
        var n2Task = await Db.Queryable<SysFlowTask>().FirstAsync(t => t.InstanceId == inst && t.NodeCode == "n2");
        Assert.Equal(FlowTaskStatus.AutoPassed, n2Task.Status);

        // 新实例：n1 B 通过后、在 n1 上制造 return 记录——改用先驳回到 start 再重走的路径：
        // 提交 exp2 → n1 待办 B → 但 return 需要 pending 任务；B 在 n1 pending 时驳回到 start 需别的审批人。
        // 简化路径：C 参与 n1 orSign，由 C 驳回到 start（B 未通过，不触发重复语义）→ 重提 → B 重审 n1 通过 →
        // 因存在 return 记录，n2 的 B 不再自动通过，而是重新生成待办。
        var c = await EnsureUserAsync("t_flow_rt_c", 4);
        var graph2 = $$"""
        {"nodes":[
          {"code":"start","type":"start","next":"n1"},
          {"code":"n1","type":"approval","name":"初审","mode":"countersign","next":"n2","approvers":[{"type":"user","userIds":["{{b.Id}}","{{c.Id}}"]}]},
          {"code":"n2","type":"approval","name":"复审","mode":"orSign","next":"end","approvers":[{"type":"user","userIds":["{{b.Id}}"]}]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (_, code2) = await CreateAndEnableAsync(graph2);
        await BindAsync("biz_expense", code2);
        var exp2 = await NewExpenseAsync(70, 1, "admin", 2);
        long inst2;
        using (As(1, "admin", 2))
            inst2 = await Fx.Engine().SubmitAsync(new FlowSubmitDto
            {
                BusinessTable = "biz_expense", BusinessId = exp2.Id, Variables = new Dictionary<string, object> { ["amount"] = 70 }
            }, 1, "admin");

        var bt = await Db.Queryable<SysFlowTask>().FirstAsync(t => t.InstanceId == inst2 && t.ApproverUserId == b.Id);
        var ct = await Db.Queryable<SysFlowTask>().FirstAsync(t => t.InstanceId == inst2 && t.ApproverUserId == c.Id);
        using (As(b.Id, b.UserName, 3))
            await Fx.Engine().HandleAsync(bt.Id, new FlowActDto { Action = "approve" }, b.Id, "B"); // B 已过 n1
        using (As(c.Id, c.UserName, 4))
            await Fx.Engine().ReturnAsync(ct.Id, new FlowReturnDto { TargetNodeCode = "start", Comment = "补材料" }, c.Id, "C");

        var resubmit = await Db.Queryable<SysFlowTask>().FirstAsync(t =>
            t.InstanceId == inst2 && t.Status == FlowTaskStatus.Pending && t.NodeCode == "start");
        Assert.NotNull(resubmit);
        Assert.Equal(1, resubmit.ApproverUserId); // start 重提任务归发起人 admin

        using (As(1, "admin", 2))
            await Fx.Engine().HandleAsync(resubmit.Id, new FlowActDto { Action = "approve", Comment = "已补充" }, 1, "admin");
        // 重走 n1：B、C 都要重新待办（return 关闭重复自动通过——B 之前 Agreed 也不跳过）
        var ren1 = await Db.Queryable<SysFlowTask>().Where(t =>
            t.InstanceId == inst2 && t.NodeCode == "n1" && t.Status == FlowTaskStatus.Pending).ToListAsync();
        Assert.Equal(2, ren1.Count(t => t.ApproverUserId == b.Id || t.ApproverUserId == c.Id));
    }

    [Fact]
    public async Task Withdraw_Only_Submitter_When_Nothing_Handled()
    {
        var wang = await EnsureUserAsync("t_flow_wd", 3);
        var (_, code) = await CreateAndEnableAsync(OrSignGraph(wang.Id.ToString()));
        await BindAsync("biz_expense", code);
        var exp = await NewExpenseAsync(80, 1, "admin", 2);
        long instanceId;
        using (As(1, "admin", 2))
            instanceId = await Fx.Engine().SubmitAsync(new FlowSubmitDto
            {
                BusinessTable = "biz_expense", BusinessId = exp.Id, Variables = new Dictionary<string, object> { ["amount"] = 80 }
            }, 1, "admin");

        using (As(999, "other", 2))
            await Assert.ThrowsAnyAsync<BizException>(async () => await Fx.Engine().WithdrawAsync(instanceId, 999));

        using (As(1, "admin", 2))
            await Fx.Engine().WithdrawAsync(instanceId, 1);
        var inst = await Db.Queryable<SysFlowInstance>().InSingleAsync(instanceId);
        Assert.Equal(FlowInstanceStatus.Withdrawn, inst.Status);
        Assert.False(await Db.Queryable<SysFlowTask>().AnyAsync(t => t.InstanceId == instanceId && t.Status == FlowTaskStatus.Pending));
    }

    [Fact]
    public async Task Transfer_And_AddSign_Before_And_After()
    {
        var u1 = await EnsureUserAsync("t_flow_tr1", 3);
        var u2 = await EnsureUserAsync("t_flow_tr2", 4);
        var (_, code) = await CreateAndEnableAsync(OrSignGraph(u1.Id.ToString()));
        await BindAsync("biz_expense", code);
        var exp = await NewExpenseAsync(90, 1, "admin", 2);
        long instanceId;
        using (As(1, "admin", 2))
            instanceId = await Fx.Engine().SubmitAsync(new FlowSubmitDto
            {
                BusinessTable = "biz_expense", BusinessId = exp.Id, Variables = new Dictionary<string, object> { ["amount"] = 90 }
            }, 1, "admin");
        var task = await Db.Queryable<SysFlowTask>().FirstAsync(t => t.InstanceId == instanceId);

        using (As(u1.Id, u1.UserName, 3))
        {
            await Fx.Engine().TransferAsync(task.Id, new FlowTransferDto { ToUserId = u2.Id }, u1.Id, "tr1");
        }
        Assert.Equal(FlowTaskStatus.Transferred, (await Db.Queryable<SysFlowTask>().InSingleAsync(task.Id)).Status);
        var newTask = await Db.Queryable<SysFlowTask>().FirstAsync(t =>
            t.InstanceId == instanceId && t.ApproverUserId == u2.Id && t.Status == FlowTaskStatus.Pending);
        Assert.NotNull(newTask);

        // 后加签：入队 AppendNodesJson
        using (As(u2.Id, u2.UserName, 4))
        {
            await Fx.Engine().AddSignAsync(newTask.Id, new FlowAddSignDto
            {
                After = true, UserIds = [u1.Id], Comment = "补充审核"
            }, u2.Id, "tr2");
            // u2 通过 → 应先进加签节点而非 end
            await Fx.Engine().HandleAsync(newTask.Id, new FlowActDto { Action = "approve" }, u2.Id, "tr2");
        }

        var inst = await Db.Queryable<SysFlowInstance>().InSingleAsync(instanceId);
        Assert.Equal(FlowInstanceStatus.Running, inst.Status); // 停在加签节点
        var appendTask = await Db.Queryable<SysFlowTask>().FirstAsync(t =>
            t.InstanceId == instanceId && t.Status == FlowTaskStatus.Pending);
        Assert.Equal(u1.Id, appendTask.ApproverUserId);
        using (As(u1.Id, u1.UserName, 3))
            await Fx.Engine().HandleAsync(appendTask.Id, new FlowActDto { Action = "approve" }, u1.Id, "tr1");
        Assert.Equal(FlowInstanceStatus.Approved, (await Db.Queryable<SysFlowInstance>().InSingleAsync(instanceId)).Status);
    }

    [Fact]
    public async Task Condition_Branching_Routes_By_Amount()
    {
        var low = await EnsureUserAsync("t_flow_lo", 3);
        var high = await EnsureUserAsync("t_flow_hi", 4);
        var graph = $$"""
        {"nodes":[
          {"code":"start","type":"start","next":"c1"},
          {"code":"c1","type":"condition","name":"分流","branches":[{"name":"小额","priority":1,"next":"n_low","conditions":[{"variable":"amount","op":"lt","value":100}]}],"defaultNext":"n_high"},
          {"code":"n_low","type":"approval","name":"小额审","mode":"orSign","next":"end","approvers":[{"type":"user","userIds":["{{low.Id}}"]}]},
          {"code":"n_high","type":"approval","name":"大额审","mode":"orSign","next":"end","approvers":[{"type":"user","userIds":["{{high.Id}}"]}]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (_, code) = await CreateAndEnableAsync(graph);
        await BindAsync("biz_expense", code);

        var e1 = await NewExpenseAsync(50, 1, "admin", 2);
        long i1;
        using (As(1, "admin", 2))
            i1 = await Fx.Engine().SubmitAsync(new FlowSubmitDto
            {
                BusinessTable = "biz_expense", BusinessId = e1.Id, Variables = new Dictionary<string, object> { ["amount"] = 50 }
            }, 1, "admin");
        Assert.Equal("n_low", (await Db.Queryable<SysFlowInstance>().InSingleAsync(i1)).CurrentNodeCode);

        var e2 = await NewExpenseAsync(500, 1, "admin", 2);
        long i2;
        using (As(1, "admin", 2))
            i2 = await Fx.Engine().SubmitAsync(new FlowSubmitDto
            {
                BusinessTable = "biz_expense", BusinessId = e2.Id, Variables = new Dictionary<string, object> { ["amount"] = 500 }
            }, 1, "admin");
        Assert.Equal("n_high", (await Db.Queryable<SysFlowInstance>().InSingleAsync(i2)).CurrentNodeCode);
    }

    [Fact]
    public async Task Cc_Writes_Inbox_And_Notifies()
    {
        var wang = await EnsureUserAsync("t_flow_cc", 3);
        var graph = $$"""
        {"nodes":[
          {"code":"start","type":"start","next":"n1"},
          {"code":"n1","type":"approval","name":"审","mode":"orSign","next":"cc1","approvers":[{"type":"user","userIds":["1"]}]},
          {"code":"cc1","type":"cc","name":"抄送","next":"end","ccUserIds":["{{wang.Id}}"]},
          {"code":"end","type":"end"}],"entry":"start"}
        """;
        var (_, code) = await CreateAndEnableAsync(graph);
        await BindAsync("biz_expense", code);
        var exp = await NewExpenseAsync(110, 1, "admin", 2);
        long instanceId;
        using (As(1, "admin", 2))
            instanceId = await Fx.Engine().SubmitAsync(new FlowSubmitDto
            {
                BusinessTable = "biz_expense", BusinessId = exp.Id, Variables = new Dictionary<string, object> { ["amount"] = 110 }
            }, 1, "admin");

        // admin=发起人 → 自动通过 → 直达 cc
        Assert.NotNull(await Db.Queryable<SysFlowCc>().FirstAsync(c => c.InstanceId == instanceId && c.UserId == wang.Id));
        Assert.Contains(Fx.Notify.Notices, n => n.Uid == wang.Id && n.Title.StartsWith("抄送"));
    }

    // ---------------- helper ----------------
    private async Task<SysUser> EnsureUserAsync(string userName, long deptId)
    {
        var u = await Db.Queryable<SysUser>().FirstAsync(x => x.UserName == userName);
        if (u is not null) return u;
        u = new SysUser
        {
            Id = SnowflakeId.NextId(), UserName = userName, NickName = userName, DeptId = deptId,
            Password = Panshi.Common.Security.PasswordHasher.Hash("Test@123456"),
            Status = EnableStatus.Enabled, PwdUpdateTime = DateTime.Now, OwnerUserId = SnowflakeId.NextId(),
            CreateTime = DateTime.Now
        };
        u.OwnerUserId = u.Id;
        await Db.Insertable(u).ExecuteCommandAsync();
        return u;
    }
}
