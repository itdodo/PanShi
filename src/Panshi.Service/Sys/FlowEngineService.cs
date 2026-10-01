using System.Text.Json;
using Panshi.Common.Exceptions;
using Panshi.Common.Realtime;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Flow;

namespace Panshi.Service.Sys;

/// <summary>
/// 审批流引擎（蓝图§5.7，抄钉钉语义）。架构约束：
/// 公开方法各自包事务（Tran.RunAsync）；内部方法禁止再开事务（红线 #3）。
/// </summary>
public class FlowEngineService(
    IRepository<SysFlowInstance> instanceRepo,
    IRepository<SysFlowTask> taskRepo,
    IRepository<SysFlowRecord> recordRepo,
    IRepository<SysFlowCc> ccRepo,
    IRepository<SysFlowDefinition> defRepo,
    IRepository<SysFlowBinding> bindingRepo,
    IRepository<SysUser> userRepo,
    IRepository<SysDept> deptRepo,
    IRepository<SysRole> roleRepo,
    IRepository<SysUserRole> userRoleRepo,
    IRepository<SysPosition> posRepo,
    IRepository<SysUserPosition> userPosRepo,
    INotifyService notify,
    IEnumerable<IFlowBusinessHandler> handlers)
{
    private static readonly JsonSerializerOptions Json = Common.Json.JsonConfig.Options;

    private Dictionary<string, IFlowBusinessHandler> HandlerMap =>
        handlers.ToDictionary(h => h.BusinessTable, StringComparer.OrdinalIgnoreCase);

    // ================= 提交 =================
    public async Task<long> SubmitAsync(FlowSubmitDto dto, long submitterId, string submitterName)
    {
        var handler = HandlerMap.TryGetValue(dto.BusinessTable, out var h) ? h : null;

        var binding = await bindingRepo.FindAsync(b => b.BusinessTable == dto.BusinessTable && b.Status == 1);
        if (binding is null) return -1; // 未绑定/停用=不走审批（调用方直接置业务状态）

        var def = await defRepo.FindAsync(d => d.FlowCode == binding.FlowCode && d.Status == 1)
            ?? throw new BizException($"流程 {binding.FlowCode} 无启用版本");
        var graph = FlowGraphValidator.Parse(def.NodeJson);
        var (valid, msg) = FlowGraphValidator.Validate(def.NodeJson);
        if (!valid) throw new BizException($"流程 DSL 非法：{msg}");

        if (await instanceRepo.ExistsAsync(i =>
                i.BusinessTable == dto.BusinessTable && i.BusinessId == dto.BusinessId &&
                i.Status == FlowInstanceStatus.Running))
            throw new BizException("该单据已有审批中实例");

        var submitter = await userRepo.GetAsync(submitterId);
        var variables = new Dictionary<string, object?>(dto.Variables.Count + 1);
        foreach (var (k, v) in dto.Variables) variables[k] = v;
        if (dto.ChoiceUserIds.Count > 0) variables["_choice"] = dto.ChoiceUserIds;

        var summary = handler is null ? null : await handler.GetSummaryAsync(dto.BusinessId);

        var instance = new SysFlowInstance
        {
            FlowCode = def.FlowCode, DefinitionId = def.Id, BusinessTable = dto.BusinessTable,
            BusinessId = dto.BusinessId, Summary = summary, Status = FlowInstanceStatus.Running,
            VariablesJson = JsonSerializer.Serialize(variables, Json),
            SubmitterId = submitterId, SubmitterName = submitterName, SubmitterDeptId = submitter.DeptId
        };

        await Tran.RunAsync(instanceRepo.Db, async () =>
        {
            await instanceRepo.InsertAsync(instance);
            await AddRecordAsync(instance.Id, null, "提交", FlowConstants.Action.Submit, submitterId, submitterName, null);
            await WalkFromAsync(instance, graph, graph.Entry);
        });
        return instance.Id;
    }

    /// <summary>
    /// 实例是否仍在跑。提交这一次就可能直接走到终态（节点全自动通过、自选没选人、审批人全是发起人自己），
    /// 业务侧据此决定要不要自己写 Status——已终态时终态是 OnFinishedAsync 落的，再写一遍就是覆盖。
    /// </summary>
    public Task<bool> IsOpenAsync(long instanceId)
        => instanceRepo.ExistsAsync(i => i.Id == instanceId && i.Status == FlowInstanceStatus.Running);

    // ================= 审批动作 =================
    public async Task HandleAsync(long taskId, FlowActDto dto, long actorId, string actorName)
    {
        var action = dto.Action.ToLowerInvariant();
        if (action is not ("approve" or "reject")) throw new BizException("动作仅支持 approve/reject");

        await Tran.RunAsync(instanceRepo.Db, async () =>
        {
            var task = await LoadPendingTaskAsync(taskId, actorId);
            var instance = await instanceRepo.GetAsync(task.InstanceId);
            if (instance.Status != FlowInstanceStatus.Running) throw new BizException("实例已结束");
            var graph = FlowGraphValidator.Parse((await defRepo.GetAsync(instance.DefinitionId)).NodeJson);
            var node = graph.Find(task.NodeCode);

            task.Status = action == "approve" ? FlowTaskStatus.Agreed : FlowTaskStatus.Rejected;
            task.Comment = dto.Comment;
            task.HandledTime = DateTime.Now;
            await taskRepo.UpdateColumnsAsync(task, "Status", "Comment", "HandledTime");
            await AddRecordAsync(instance.Id, task.NodeCode, task.NodeName,
                action == "approve" ? FlowConstants.Action.Approve : FlowConstants.Action.Reject,
                actorId, actorName, dto.Comment);

            if (action == "reject")
            {
                await FinishAsync(instance, FlowInstanceStatus.Rejected, graph, actorId, actorName);
                return;
            }

            // 重提交任务（start 节点）：从入口重走
            if (task.NodeCode == FlowConstants.NodeType.Start)
            {
                await InvalidatePendingAsync(instance.Id);
                await WalkFromAsync(instance, graph, graph.Entry);
                return;
            }

            var stillOpen = await taskRepo.ListAsync(t =>
                t.InstanceId == instance.Id && t.NodeCode == task.NodeCode &&
                (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Waiting));

            if (stillOpen.Count > 0)
            {
                if (task.NodeMode == FlowConstants.NodeMode.OrSign)
                {
                    // 或签一人定局：其余作废后立即前进
                    foreach (var open in stillOpen.Where(t => t.Id != task.Id))
                    {
                        open.Status = FlowTaskStatus.Invalidated;
                        await taskRepo.UpdateColumnsAsync(open, "Status");
                    }

                    await AdvanceFromNodeAsync(instance, graph, task.NodeCode);
                    return;
                }

                if (task.NodeMode == FlowConstants.NodeMode.Sequential)
                {
                    var nextSeq = stillOpen.Where(t => t.Id != task.Id).OrderBy(t => t.Sequence).FirstOrDefault();
                    if (nextSeq is not null)
                    {
                        nextSeq.Status = FlowTaskStatus.Pending;
                        await taskRepo.UpdateColumnsAsync(nextSeq, "Status");
                        await NotifyUsersIfAnyAsync([nextSeq.ApproverUserId], $"待办：{instance.Summary ?? task.NodeName}",
                            nextSeq.NodeName, instance);
                        return;
                    }

                    // 最后一个依次任务完成 → 节点通过
                    await AdvanceFromNodeAsync(instance, graph, task.NodeCode);
                }

                return; // 会签仍有待办
            }

            await AdvanceFromNodeAsync(instance, graph, task.NodeCode);
        });
    }

    /// <summary>驳回至节点（Return）：作废全部待办→目标节点重新生成待办；实例保持 Running。</summary>
    public async Task ReturnAsync(long taskId, FlowReturnDto dto, long actorId, string actorName)
    {
        await Tran.RunAsync(instanceRepo.Db, async () =>
        {
            var task = await LoadPendingTaskAsync(taskId, actorId);
            var instance = await instanceRepo.GetAsync(task.InstanceId);
            var graph = FlowGraphValidator.Parse((await defRepo.GetAsync(instance.DefinitionId)).NodeJson);
            if (dto.TargetNodeCode != FlowConstants.NodeType.Start &&
                graph.Find(dto.TargetNodeCode) is not { Type: FlowConstants.NodeType.Approval })
                throw new BizException("驳回目标必须是审批节点或 start");

            task.Status = FlowTaskStatus.Returned;
            task.Comment = dto.Comment;
            task.HandledTime = DateTime.Now;
            await taskRepo.UpdateColumnsAsync(task, "Status", "Comment", "HandledTime");

            // 作废其余待办/等待
            var pendings = await taskRepo.ListAsync(t => t.InstanceId == instance.Id && t.Id != task.Id &&
                (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Waiting));
            foreach (var p in pendings)
            {
                p.Status = FlowTaskStatus.Invalidated;
                await taskRepo.UpdateColumnsAsync(p, "Status");
            }

            await AddRecordAsync(instance.Id, task.NodeCode, task.NodeName, FlowConstants.Action.Return,
                actorId, actorName, dto.Comment, JsonSerializer.Serialize(new { target = dto.TargetNodeCode }, Json));

            if (dto.TargetNodeCode == FlowConstants.NodeType.Start)
            {
                // 发起人生成「重新提交」待办
                var resubmit = new SysFlowTask
                {
                    InstanceId = instance.Id, NodeCode = FlowConstants.NodeType.Start, NodeName = "重新提交",
                    NodeMode = FlowConstants.NodeMode.OrSign, ApproverUserId = instance.SubmitterId,
                    ApproverName = instance.SubmitterName, Status = FlowTaskStatus.Pending
                };
                await taskRepo.InsertAsync(resubmit);
                instance.CurrentNodeCode = FlowConstants.NodeType.Start;
                await instanceRepo.UpdateColumnsAsync(instance, "CurrentNodeCode");
                await NotifyUsersIfAnyAsync([instance.SubmitterId], $"待重新提交：{instance.Summary}", "重新提交", instance);
                return;
            }

            instance.CurrentNodeCode = dto.TargetNodeCode;
            await instanceRepo.UpdateColumnsAsync(instance, "CurrentNodeCode");
            var variables = ParseVariables(instance);
            await CreateTasksForNodeAsync(instance, graph, graph.Find(dto.TargetNodeCode)!, variables);
        });
    }

    /// <summary>转办：原任务 Transferred + 新人生成待办。</summary>
    public async Task TransferAsync(long taskId, FlowTransferDto dto, long actorId, string actorName)
    {
        await Tran.RunAsync(instanceRepo.Db, async () =>
        {
            var task = await LoadPendingTaskAsync(taskId, actorId);
            var instance = await instanceRepo.GetAsync(task.InstanceId);
            var target = await userRepo.GetAsync(dto.ToUserId);

            task.Status = FlowTaskStatus.Transferred;
            task.Comment = dto.Comment;
            task.HandledTime = DateTime.Now;
            await taskRepo.UpdateColumnsAsync(task, "Status", "Comment", "HandledTime");
            await taskRepo.InsertAsync(new SysFlowTask
            {
                InstanceId = instance.Id, NodeCode = task.NodeCode, NodeName = task.NodeName,
                NodeMode = task.NodeMode, ApproverUserId = target.Id, ApproverName = target.NickName,
                Status = FlowTaskStatus.Pending, Sequence = task.Sequence
            });
            await AddRecordAsync(instance.Id, task.NodeCode, task.NodeName, FlowConstants.Action.Transfer,
                actorId, actorName, dto.Comment, JsonSerializer.Serialize(new { to = target.Id.ToString() }, Json));
            await NotifyUsersIfAnyAsync([target.Id], $"待办（转办给你）：{instance.Summary}", task.NodeName, instance);
        });
    }

    /// <summary>加签：前加签=并入当前节点共同把关；后加签=AppendNodesJson 队列本节点后先进。</summary>
    public async Task AddSignAsync(long taskId, FlowAddSignDto dto, long actorId, string actorName)
    {
        await Tran.RunAsync(instanceRepo.Db, async () =>
        {
            var task = await LoadPendingTaskAsync(taskId, actorId);
            var instance = await instanceRepo.GetAsync(task.InstanceId);
            var users = await userRepo.ListAsync(u => dto.UserIds.Contains(u.Id));
            if (users.Count == 0) throw new BizException("加签人不存在");

            if (!dto.After)
            {
                foreach (var u in users.Where(u => u.Id != task.ApproverUserId))
                    await taskRepo.InsertAsync(new SysFlowTask
                    {
                        InstanceId = instance.Id, NodeCode = task.NodeCode, NodeName = task.NodeName + "（前加签）",
                        NodeMode = FlowConstants.NodeMode.Countersign, ApproverUserId = u.Id, ApproverName = u.NickName,
                        Status = FlowTaskStatus.Pending
                    });
            }
            else
            {
                var append = ParseAppend(instance);
                append.AddRange(users.Select(u => new AppendNodeDto
                {
                    Code = $"append_{SnowflakeId.NextId():x}", Name = $"{actorName}加签（{u.NickName}）",
                    UserId = u.Id, UserName = u.NickName
                }));
                instance.AppendNodesJson = JsonSerializer.Serialize(append, Json);
                await instanceRepo.UpdateColumnsAsync(instance, "AppendNodesJson");
            }

            await AddRecordAsync(instance.Id, task.NodeCode, task.NodeName, FlowConstants.Action.AddSign,
                actorId, actorName, dto.Comment,
                JsonSerializer.Serialize(new { after = dto.After, users = users.Select(u => u.Id.ToString()) }, Json));
            if (!dto.After)
                await NotifyUsersIfAnyAsync(users.Select(u => u.Id).ToList(), $"待办（加签）：{instance.Summary}",
                    task.NodeName, instance);
        });
    }

    /// <summary>撤回：仅发起人且无人处理过。</summary>
    public async Task WithdrawAsync(long instanceId, long actorId)
    {
        await Tran.RunAsync(instanceRepo.Db, async () =>
        {
            var instance = await instanceRepo.GetAsync(instanceId);
            if (instance.SubmitterId != actorId) throw BizException.Forbidden("仅发起人可撤回");
            if (instance.Status != FlowInstanceStatus.Running) throw new BizException("实例已结束");
            // 「无人处理过」按人工任务状态判定（条件/自动通过记录不阻塞撤回）
            if (await taskRepo.ExistsAsync(t => t.InstanceId == instanceId &&
                    (t.Status == FlowTaskStatus.Agreed || t.Status == FlowTaskStatus.Rejected ||
                     t.Status == FlowTaskStatus.Transferred || t.Status == FlowTaskStatus.Returned)))
                throw new BizException("已有人处理，不能撤回");

            await FinishCoreAsync(instance, FlowInstanceStatus.Withdrawn, null, instance.SubmitterId,
                instance.SubmitterName, FlowConstants.Action.Withdraw, "发起人撤回");
        });
    }

    /// <summary>作废（管理员 workflow:instance:void）。</summary>
    public async Task VoidAsync(long instanceId, long actorId, string reason)
    {
        await Tran.RunAsync(instanceRepo.Db, async () =>
        {
            var instance = await instanceRepo.GetAsync(instanceId);
            if (instance.Status != FlowInstanceStatus.Running) throw new BizException("实例已结束");
            await FinishCoreAsync(instance, FlowInstanceStatus.Voided, null, actorId,
                instance.SubmitterName, FlowConstants.Action.Void, reason);
            await NotifyUsersIfAnyAsync([instance.SubmitterId], $"单据已作废：{instance.Summary}", "作废", instance);
        });
    }

    // ================= 内部推进 =================
    private async Task<SysFlowTask> LoadPendingTaskAsync(long taskId, long actorId)
    {
        var task = await taskRepo.GetAsync(taskId);
        if (task.ApproverUserId != actorId) throw BizException.Forbidden("只能处理自己的待办");
        if (task.Status != FlowTaskStatus.Pending) throw new BizException("任务已处理");
        return task;
    }

    /// <summary>从节点通过后前进：先出后加签队列，再走图。</summary>
    private async Task AdvanceFromNodeAsync(SysFlowInstance instance, FlowGraph graph, string nodeCode)
    {
        var node = graph.Find(nodeCode);
        var append = ParseAppend(instance);
        if (append.Count > 0)
        {
            var first = append[0];
            append.RemoveAt(0);
            instance.AppendNodesJson = append.Count > 0 ? JsonSerializer.Serialize(append, Json) : null;
            instance.CurrentNodeCode = first.Code;
            await instanceRepo.UpdateColumnsAsync(instance, "AppendNodesJson", "CurrentNodeCode");
            await CreateAppendTaskAsync(instance, first);
            return;
        }

        var next = node?.Next;
        if (string.IsNullOrEmpty(next))
        {
            await FinishAsync(instance, FlowInstanceStatus.Approved, graph, null, null);
            return;
        }

        await WalkFromAsync(instance, graph, next);
    }

    /// <summary>沿图推进直到审批节点停留或 end 结束（循环解释执行）。</summary>
    private async Task WalkFromAsync(SysFlowInstance instance, FlowGraph graph, string code)
    {
        var variables = ParseVariables(instance);
        while (true)
        {
            var node = graph.Find(code) ?? throw new BizException($"节点缺失 {code}");
            switch (node.Type)
            {
                case FlowConstants.NodeType.End:
                    await FinishAsync(instance, FlowInstanceStatus.Approved, graph, null, null);
                    return;
                case FlowConstants.NodeType.Start:
                case FlowConstants.NodeType.Cc:
                    if (node.Type == FlowConstants.NodeType.Cc) await DoCcAsync(instance, node);
                    code = node.Next!;
                    continue;
                case FlowConstants.NodeType.Condition:
                    var (next, hit) = ConditionEvaluator.Pick(node, variables);
                    await AddRecordAsync(instance.Id, node.Code, node.Name ?? "条件分支", FlowConstants.Action.Auto,
                        null, "系统", hit is null ? "走默认分支" : $"命中分支：{hit.Name}");
                    if (string.IsNullOrEmpty(next))
                    {
                        await FinishAsync(instance, FlowInstanceStatus.Approved, graph, null, null);
                        return;
                    }

                    code = next;
                    continue;
                case FlowConstants.NodeType.Approval:
                    instance.CurrentNodeCode = node.Code;
                    await instanceRepo.UpdateColumnsAsync(instance, "CurrentNodeCode");
                    await CreateTasksForNodeAsync(instance, graph, node, variables);
                    return;
                default:
                    throw new BizException($"未知节点类型 {node.Type}");
            }
        }
    }

    private async Task CreateTasksForNodeAsync(SysFlowInstance instance, FlowGraph graph, FlowNodeDto node,
        Dictionary<string, object?> variables)
    {
        var approverIds = await ResolveApproversAsync(node, instance, variables);
        var mode = string.IsNullOrEmpty(node.Mode) ? FlowConstants.NodeMode.OrSign : node.Mode;

        // 自动通过判定基准：发起人 / 本实例已审人重复（⚠️ 退回后置 Return 记录存在时关闭）/ 审批人为空
        var hasReturn = await recordRepo.ExistsAsync(r => r.InstanceId == instance.Id && r.Action == FlowConstants.Action.Return);
        var alreadyHandled = (await taskRepo.ListAsync(t => t.InstanceId == instance.Id &&
                (t.Status == FlowTaskStatus.Agreed || t.Status == FlowTaskStatus.AutoPassed)))
            .Select(t => t.ApproverUserId).ToHashSet();

        if (approverIds.Count == 0)
        {
            await AddRecordAsync(instance.Id, node.Code, node.Name, FlowConstants.Action.Auto, null, "系统",
                "审批人为空，自动通过");
            await AdvanceFromNodeAsync(instance, graph, node.Code);
            return;
        }

        var autoPassed = new List<long>();
        var effective = new List<long>();
        foreach (var uid in approverIds)
        {
            if (uid == instance.SubmitterId || (!hasReturn && alreadyHandled.Contains(uid))) autoPassed.Add(uid);
            else if (!effective.Contains(uid)) effective.Add(uid);
        }

        foreach (var uid in autoPassed)
        {
            var u = await userRepo.FindAsync(uid);
            await taskRepo.InsertAsync(new SysFlowTask
            {
                InstanceId = instance.Id, NodeCode = node.Code, NodeName = node.Name ?? node.Code, NodeMode = mode,
                ApproverUserId = uid, ApproverName = u?.NickName ?? "", Status = FlowTaskStatus.AutoPassed,
                HandledTime = DateTime.Now
            });
            await AddRecordAsync(instance.Id, node.Code, node.Name, FlowConstants.Action.Auto, uid,
                autoPassed.Contains(uid) ? (await userRepo.FindAsync(uid))?.NickName : "", "自动通过");
        }

        if (effective.Count == 0)
        {
            await AdvanceFromNodeAsync(instance, graph, node.Code);
            return;
        }

        var seq = 0;
        var created = new List<SysFlowTask>();
        foreach (var uid in effective)
        {
            var u = await userRepo.FindAsync(uid);
            created.Add(new SysFlowTask
            {
                InstanceId = instance.Id, NodeCode = node.Code, NodeName = node.Name ?? node.Code, NodeMode = mode,
                ApproverUserId = uid, ApproverName = u?.NickName ?? "", Sequence = seq,
                Status = mode == FlowConstants.NodeMode.Sequential && seq > 0
                    ? FlowTaskStatus.Waiting : FlowTaskStatus.Pending
            });
            seq++;
        }

        await taskRepo.InsertRangeAsync(created);
        await NotifyUsersIfAnyAsync(created.Where(t => t.Status == FlowTaskStatus.Pending).Select(t => t.ApproverUserId).ToList(),
            $"待办：{instance.Summary}", node.Name, instance);
    }

    private async Task CreateAppendTaskAsync(SysFlowInstance instance, AppendNodeDto append)
    {
        await taskRepo.InsertAsync(new SysFlowTask
        {
            InstanceId = instance.Id, NodeCode = append.Code, NodeName = append.Name,
            NodeMode = FlowConstants.NodeMode.OrSign, ApproverUserId = append.UserId,
            ApproverName = append.UserName, Status = FlowTaskStatus.Pending
        });
        await NotifyUsersIfAnyAsync([append.UserId], $"待办（加签）：{instance.Summary}", append.Name, instance);
    }

    /// <summary>审批人解析：user/role/position(company|submitterDept)/deptLeader/submitterChoice，多规则并集。</summary>
    private async Task<List<long>> ResolveApproversAsync(FlowNodeDto node, SysFlowInstance instance,
        Dictionary<string, object?> variables)
    {
        var result = new HashSet<long>();
        foreach (var ap in node.Approvers ?? [])
        {
            switch (ap.Type)
            {
                case FlowConstants.ApproverType.User:
                    foreach (var s in ap.UserIds ?? [])
                        if (long.TryParse(s, out var uid)) result.Add(uid);
                    break;
                case FlowConstants.ApproverType.Role:
                    {
                        var roleCodesList = ap.RoleCodes ?? new List<string>();
                        var roleIds = (await roleRepo.ListAsync(r => roleCodesList.Contains(r.RoleCode)))
                            .Select(r => r.Id).ToHashSet();
                        var links = await userRoleRepo.ListAsync(l => roleIds.Contains(l.RoleId));
                        foreach (var l in links) result.Add(l.UserId);
                        break;
                    }
                case FlowConstants.ApproverType.Position:
                    {
                        var posCodesList = ap.PositionCodes ?? new List<string>();
                        var posIds = (await posRepo.ListAsync(p => posCodesList.Contains(p.PositionCode)))
                            .Select(p => p.Id).ToHashSet();
                        var links = await userPosRepo.ListAsync(l => posIds.Contains(l.PositionId));
                        var userIds = links.Select(l => l.UserId).ToHashSet();
                        if (ap.Scope == FlowConstants.ApproverScope.SubmitterDept && instance.SubmitterDeptId is long sd)
                        {
                            var deptIds = new List<long> { sd };
                            foreach (var uid2 in userIds)
                            {
                                var u = await userRepo.FindAsync(uid2);
                                if (u?.DeptId is long ud && deptIds.Contains(ud)) result.Add(uid2);
                            }
                        }
                        else
                        {
                            foreach (var uid2 in userIds) result.Add(uid2);
                        }

                        break;
                    }
                case FlowConstants.ApproverType.DeptLeader:
                    {
                        long? deptId = ap.DeptId is > 0 ? ap.DeptId : instance.SubmitterDeptId;
                        // 逐级上找带头人的部门
                        while (deptId is long did)
                        {
                            var dept = await deptRepo.FindAsync(did);
                            if (dept is null) break;
                            if (dept.LeaderUserId is long leader) { result.Add(leader); break; }
                            deptId = dept.ParentId;
                        }

                        break;
                    }
                case FlowConstants.ApproverType.SubmitterChoice:
                    if (variables.TryGetValue("_choice", out var choice) && choice is not null)
                    {
                        var list = choice is JsonElement je ? je.EnumerateArray().Select(e => e.ToString()).ToList()
                            : (choice as IEnumerable<object>)?.Select(o => o.ToString() ?? "").ToList() ?? [];
                        foreach (var s in list)
                            if (long.TryParse(s, out var uid)) result.Add(uid);
                    }

                    break;
            }
        }

        return result.ToList();
    }

    private async Task DoCcAsync(SysFlowInstance instance, FlowNodeDto node)
    {
        var userIds = new HashSet<long>();
        foreach (var s in node.CcUserIds ?? [])
            if (long.TryParse(s, out var uid)) userIds.Add(uid);
        var roleCodes = node.CcRoleCodes ?? new List<string>();
        if (roleCodes.Count > 0)
        {
            var roleIds = (await roleRepo.ListAsync(r => roleCodes.Contains(r.RoleCode))).Select(r => r.Id).ToHashSet();
            foreach (var l in await userRoleRepo.ListAsync(l => roleIds.Contains(l.RoleId))) userIds.Add(l.UserId);
        }

        foreach (var uid in userIds)
        {
            var u = await userRepo.FindAsync(uid);
            await ccRepo.InsertAsync(new SysFlowCc
            {
                InstanceId = instance.Id, NodeCode = node.Code, UserId = uid, UserName = u?.NickName ?? ""
            });
        }

        await AddRecordAsync(instance.Id, node.Code, node.Name ?? "抄送", FlowConstants.Action.Cc, null, "系统", null);
        await NotifyUsersIfAnyAsync(userIds.ToList(), $"抄送：{instance.Summary}", node.Name ?? "抄送", instance);
    }

    private async Task FinishAsync(SysFlowInstance instance, FlowInstanceStatus status, FlowGraph? graph,
        long? actorId, string? actorName)
    {
        await FinishCoreAsync(instance, status, graph, actorId, actorName,
            status == FlowInstanceStatus.Approved ? FlowConstants.Action.Approve : FlowConstants.Action.Reject, null);

        var title = status switch
        {
            FlowInstanceStatus.Approved => "审批已通过",
            FlowInstanceStatus.Rejected => "审批被拒绝",
            _ => "审批已结束"
        };
        await NotifyUsersIfAnyAsync([instance.SubmitterId], $"{title}：{instance.Summary}", title, instance);
    }

    private async Task FinishCoreAsync(SysFlowInstance instance, FlowInstanceStatus status, FlowGraph? graph,
        long? actorId, string? actorName, string action, string? comment)
    {
        instance.Status = status;
        instance.FinishedTime = DateTime.Now;
        instance.CurrentNodeCode = null;
        await instanceRepo.UpdateColumnsAsync(instance, "Status", "FinishedTime", "CurrentNodeCode");

        // 待办/等待全部作废
        var pendings = await taskRepo.ListAsync(t => t.InstanceId == instance.Id &&
            (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Waiting));
        foreach (var p in pendings)
        {
            p.Status = FlowTaskStatus.Invalidated;
            await taskRepo.UpdateColumnsAsync(p, "Status");
        }

        await AddRecordAsync(instance.Id, null, "流程结束", action, actorId, actorName, comment);

        // 业务回调（引擎事务内执行）
        if (HandlerMap.TryGetValue(instance.BusinessTable, out var handler))
            await handler.OnFinishedAsync(instance);
    }

    private async Task InvalidatePendingAsync(long instanceId)
    {
        var pendings = await taskRepo.ListAsync(t => t.InstanceId == instanceId &&
            (t.Status == FlowTaskStatus.Pending || t.Status == FlowTaskStatus.Waiting));
        foreach (var p in pendings)
        {
            p.Status = FlowTaskStatus.Invalidated;
            await taskRepo.UpdateColumnsAsync(p, "Status");
        }
    }

    private async Task AddRecordAsync(long instanceId, string? nodeCode, string? nodeName, string action,
        long? operatorId, string? operatorName, string? comment, string? extraJson = null)
    {
        await recordRepo.InsertAsync(new SysFlowRecord
        {
            InstanceId = instanceId, NodeCode = nodeCode, NodeName = nodeName, Action = action,
            OperatorId = operatorId, OperatorName = operatorName, Comment = comment, ExtraJson = extraJson
        });
    }

    private async Task NotifyUsersIfAnyAsync(List<long> userIds, string title, string? content,
        SysFlowInstance instance)
    {
        if (userIds.Count == 0) return;
        await notify.NotifyUsersAsync(userIds, title, content, "business", "flow-task", instance.Id);
    }

    private static Dictionary<string, object?> ParseVariables(SysFlowInstance instance)
    {
        if (string.IsNullOrEmpty(instance.VariablesJson)) return new Dictionary<string, object?>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(instance.VariablesJson, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<AppendNodeDto> ParseAppend(SysFlowInstance instance)
    {
        if (string.IsNullOrEmpty(instance.AppendNodesJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<AppendNodeDto>>(instance.AppendNodesJson, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>后加签队列元素（AppendNodesJson）。</summary>
    private sealed class AppendNodeDto
    {
        public string Code { get; set; } = "";

        public string Name { get; set; } = "";

        public long UserId { get; set; }

        public string UserName { get; set; } = "";
    }
}
