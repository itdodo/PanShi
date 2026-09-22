using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using Panshi.Service.Flow;
using SqlSugar;

namespace Panshi.Service.Sys;

/// <summary>流程定义/绑定管理：FlowCode 系统自增数字串（100 起）；同编码唯一启用；绑定运行时可换。</summary>
public class FlowAdminService(
    IRepository<SysFlowDefinition> defRepo,
    IRepository<SysFlowBinding> bindingRepo,
    IRepository<SysFlowInstance> instanceRepo) : BaseService<SysFlowDefinition>(defRepo)
{
    public async Task<PagedResult<FlowDefDto>> DefPageAsync(FlowDefQuery query)
    {
        var keyword = query.Keyword?.Trim();
        var exp = Expressionable.Create<SysFlowDefinition>();
        if (!string.IsNullOrEmpty(keyword)) exp.And(d => d.FlowName.Contains(keyword) || d.FlowCode.Contains(keyword));
        if (!string.IsNullOrEmpty(query.Category)) exp.And(d => d.Category == query.Category);
        if (query.Status is not null) exp.And(d => d.Status == query.Status!.Value);
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, "create_time");
        return new PagedResult<FlowDefDto> { Total = page.Total, Rows = page.Rows.Select(ToDefDto).ToList() };
    }

    public async Task<List<string>> CategoriesAsync()
        => (await Repo.ListAsync()).Where(d => !string.IsNullOrEmpty(d.Category)).Select(d => d.Category!)
            .Distinct().ToList();

    public async Task<List<FlowDefDto>> VersionsAsync(string flowCode)
        => (await Repo.ListAsync(d => d.FlowCode == flowCode)).OrderByDescending(d => d.FlowVersion)
            .Select(ToDefDto).ToList();

    public async Task<FlowDefDto> CreateAsync(FlowDefSaveDto dto)
    {
        var (valid, msg) = FlowGraphValidator.Validate(dto.NodeJson);
        if (!valid) throw new BizException(msg);

        var code = await NextCodeAsync();
        var def = new SysFlowDefinition
        {
            FlowCode = code, FlowName = dto.FlowName, Category = dto.Category, NodeJson = dto.NodeJson,
            FlowVersion = 1, Status = 0, Remark = dto.Remark
        };
        await Repo.InsertAsync(def);
        return ToDefDto(def);
    }

    /// <summary>编辑=新版本（旧启用版自动停用；在途实例不受影响按 DefinitionId 继续）。</summary>
    public async Task<FlowDefDto> UpdateAsync(long id, FlowDefSaveDto dto)
    {
        var (valid, msg) = FlowGraphValidator.Validate(dto.NodeJson);
        if (!valid) throw new BizException(msg);
        var def = await Repo.GetAsync(id);

        var latest = (await Repo.ListAsync(d => d.FlowCode == def.FlowCode)).MaxBy(d => d.FlowVersion)!;
        var clone = new SysFlowDefinition
        {
            FlowCode = def.FlowCode, FlowName = dto.FlowName, Category = dto.Category, NodeJson = dto.NodeJson,
            FlowVersion = latest.FlowVersion + 1, Status = 0, Remark = dto.Remark
        };
        await Repo.InsertAsync(clone);
        return ToDefDto(clone);
    }

    public async Task EnableAsync(long id, int status)
    {
        var def = await Repo.GetAsync(id);
        if (status == 1)
        {
            var actives = await Repo.ListAsync(d => d.FlowCode == def.FlowCode && d.Status == 1 && d.Id != id);
            foreach (var a in actives)
            {
                a.Status = 0;
                await Repo.UpdateColumnsAsync(a, "Status");
            }

            var (valid, msg) = FlowGraphValidator.Validate(def.NodeJson);
            if (!valid) throw new BizException($"启用被拒：{msg}");
        }

        def.Status = status;
        await Repo.UpdateColumnsAsync(def, "Status");
    }

    public async Task DeleteAsync(long id)
    {
        var def = await Repo.GetAsync(id);
        if (await instanceRepo.ExistsAsync(i => i.DefinitionId == id))
            throw new BizException("该定义已有审批实例，不可删除");
        await Repo.SoftDeleteAsync(id);
    }

    // ---------------- 绑定 ----------------
    public async Task<List<FlowBindingDto>> BindingsAsync()
    {
        var defs = (await Repo.ListAsync()).ToDictionary(d => d.FlowCode, d => d.FlowName);
        return (await bindingRepo.ListAsync()).Select(b => new FlowBindingDto
        {
            Id = b.Id.ToString(), BusinessTable = b.BusinessTable, FlowCode = b.FlowCode, Status = b.Status,
            Remark = b.Remark, FlowName = defs.GetValueOrDefault(b.FlowCode), Version = b.Version
        }).ToList();
    }

    public async Task SaveBindingAsync(FlowBindingSaveDto dto)
    {
        var def = await Repo.FindAsync(d => d.FlowCode == dto.FlowCode)
            ?? throw new BizException("流程编码不存在");
        var existing = await bindingRepo.FindAsync(b => b.BusinessTable == dto.BusinessTable);
        if (existing is null)
        {
            await bindingRepo.InsertAsync(new SysFlowBinding
            {
                BusinessTable = dto.BusinessTable.Trim(), FlowCode = dto.FlowCode, Status = dto.Status,
                Remark = dto.Remark
            });
            return;
        }

        existing.FlowCode = dto.FlowCode;
        existing.Status = dto.Status;
        existing.Remark = dto.Remark;
        existing.Version = dto.Version;
        await bindingRepo.UpdateWithAuditAsync(existing, dto.Version);
        _ = def;
    }

    public async Task DeleteBindingAsync(long id) => await bindingRepo.SoftDeleteAsync(id);

    private async Task<string> NextCodeAsync()
    {
        var codes = (await Repo.ListAsync()).Select(d => d.FlowCode).ToList();
        var max = codes.Where(c => long.TryParse(c, out _)).Select(long.Parse).DefaultIfEmpty(99).Max();
        return (max + 1).ToString();
    }

    private static FlowDefDto ToDefDto(SysFlowDefinition d) => new()
    {
        Id = d.Id.ToString(), FlowCode = d.FlowCode, FlowName = d.FlowName, Category = d.Category,
        NodeJson = d.NodeJson, FlowVersion = d.FlowVersion, Status = d.Status, Remark = d.Remark,
        CreateTime = d.CreateTime, Version = d.Version
    };
}

/// <summary>审批查询：待办/已办/我发起/抄送我的/实例详情（时间线）。</summary>
public class FlowQueryService(
    IRepository<SysFlowInstance> instanceRepo,
    IRepository<SysFlowTask> taskRepo,
    IRepository<SysFlowRecord> recordRepo,
    IRepository<SysFlowCc> ccRepo,
    IRepository<SysFlowDefinition> defRepo)
{
    public async Task<PagedResult<FlowTaskDto>> TodoPageAsync(long userId, FlowTaskQuery query)
    {
        var mine = (await taskRepo.ListAsync(t => t.ApproverUserId == userId && t.Status == FlowTaskStatus.Pending))
            .OrderByDescending(t => t.CreateTime).ToList();
        var instances = await LoadInstancesAsync(mine.Select(t => t.InstanceId).ToHashSet());
        var rows = mine.Select(t => ToTaskDto(t, instances.GetValueOrDefault(t.InstanceId)!))
            .Where(x => string.IsNullOrWhiteSpace(query.Keyword) ||
                        (x.Summary?.Contains(query.Keyword.Trim(), StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
        var paged = rows.Skip((query.PageNum - 1) * query.PageSize).Take(query.PageSize).ToList();
        return new PagedResult<FlowTaskDto> { Total = rows.Count, Rows = paged };
    }

    public async Task<int> TodoCountAsync(long userId)
        => (int)await taskRepo.CountAsync(t => t.ApproverUserId == userId && t.Status == FlowTaskStatus.Pending);

    public async Task<PagedResult<FlowTaskDto>> DonePageAsync(long userId, FlowTaskQuery query)
    {
        var mine = (await taskRepo.ListAsync(t => t.ApproverUserId == userId &&
                t.Status != FlowTaskStatus.Pending && t.Status != FlowTaskStatus.Waiting))
            .OrderByDescending(t => t.HandledTime ?? t.CreateTime).ToList();
        var instances = await LoadInstancesAsync(mine.Select(t => t.InstanceId).ToHashSet());
        var rows = mine.Select(t => ToTaskDto(t, instances.GetValueOrDefault(t.InstanceId)!)).ToList();
        return new PagedResult<FlowTaskDto>
        {
            Total = rows.Count,
            Rows = rows.Skip((query.PageNum - 1) * query.PageSize).Take(query.PageSize).ToList()
        };
    }

    public async Task<PagedResult<FlowInstanceDto>> InstancePageAsync(FlowInstanceQuery query, long? submitterFilter = null)
    {
        var exp = Expressionable.Create<SysFlowInstance>();
        if (submitterFilter is long sid) exp.And(i => i.SubmitterId == sid);
        if (!string.IsNullOrEmpty(query.FlowCode)) exp.And(i => i.FlowCode == query.FlowCode);
        if (query.Status is not null) exp.And(i => i.Status == query.Status!.Value);
        var kw = query.Keyword?.Trim();
        if (!string.IsNullOrEmpty(kw)) exp.And(i => i.Summary!.Contains(kw) || i.SubmitterName.Contains(kw));

        var page = await instanceRepo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, "create_time");
        var defs = (await defRepo.ListAsync()).GroupBy(d => d.DefinitionKey()).ToDictionary(g => g.Key, g => g.First().FlowName);
        return new PagedResult<FlowInstanceDto>
        {
            Total = page.Total,
            Rows = page.Rows.Select(i => ToInstanceDto(i, defs)).ToList()
        };
    }

    public async Task<FlowInstanceDetailDto> DetailAsync(long instanceId)
    {
        var instance = await instanceRepo.GetAsync(instanceId);
        var def = await defRepo.FindAsync(d => d.Id == instance.DefinitionId);
        var tasks = (await taskRepo.ListAsync(t => t.InstanceId == instanceId))
            .OrderBy(t => t.CreateTime).ToList();
        var records = (await recordRepo.ListAsync(r => r.InstanceId == instanceId))
            .OrderBy(r => r.CreateTime).ToList();
        return new FlowInstanceDetailDto
        {
            Instance = ToInstanceDto(instance, new Dictionary<string, string>
            {
                [instance.DefinitionId.ToString()] = def?.FlowName ?? instance.FlowCode
            }),
            Tasks = tasks.Select(t => ToTaskDto(t, instance)).ToList(),
            Records = records.Select(r => new FlowRecordDto
            {
                Id = r.Id.ToString(), NodeCode = r.NodeCode, NodeName = r.NodeName, Action = r.Action,
                OperatorName = r.OperatorName, Comment = r.Comment, CreateTime = r.CreateTime
            }).ToList(),
            NodeJson = def?.NodeJson ?? "{}"
        };
    }

    public async Task<FlowInstanceDto?> ByBusinessAsync(string businessTable, long businessId)
    {
        var i = await instanceRepo.FindAsync(x => x.BusinessTable == businessTable && x.BusinessId == businessId);
        if (i is null) return null;
        var def = await defRepo.FindAsync(d => d.Id == i.DefinitionId);
        return ToInstanceDto(i, new Dictionary<string, string> { [i.DefinitionId.ToString()] = def?.FlowName ?? "" });
    }

    public async Task<PagedResult<FlowCcDto>> CcMePageAsync(long userId, FlowTaskQuery query)
    {
        var page = await ccRepo.PageAsync(c => c.UserId == userId, query.PageNum, query.PageSize, "create_time");
        var instances = await LoadInstancesAsync(page.Rows.Select(c => c.InstanceId).ToHashSet());
        var defs = (await defRepo.ListAsync()).GroupBy(d => d.DefinitionKey()).ToDictionary(g => g.Key, g => g.First().FlowName);
        return new PagedResult<FlowCcDto>
        {
            Total = page.Total,
            Rows = page.Rows.Select(c => new FlowCcDto
            {
                Id = c.Id.ToString(), InstanceId = c.InstanceId.ToString(), NodeCode = c.NodeCode,
                IsRead = c.IsRead, CreateTime = c.CreateTime,
                Instance = instances.TryGetValue(c.InstanceId, out var i) ? ToInstanceDto(i, defs) : null
            }).ToList()
        };
    }

    public async Task MarkCcReadAsync(long userId, long ccId)
    {
        var c = await ccRepo.GetAsync(ccId);
        if (c.UserId != userId) throw BizException.Forbidden("越权操作");
        if (c.IsRead) return;
        c.IsRead = true;
        c.ReadTime = DateTime.Now;
        await ccRepo.UpdateColumnsAsync(c, "IsRead", "ReadTime");
    }

    private async Task<Dictionary<long, SysFlowInstance>> LoadInstancesAsync(HashSet<long> ids)
        => (await instanceRepo.ListAsync(i => ids.Contains(i.Id))).ToDictionary(i => i.Id);

    private static FlowTaskDto ToTaskDto(SysFlowTask t, SysFlowInstance i) => new()
    {
        Id = t.Id.ToString(), InstanceId = t.InstanceId.ToString(), NodeCode = t.NodeCode, NodeName = t.NodeName,
        NodeMode = t.NodeMode, ApproverUserId = t.ApproverUserId.ToString(), ApproverName = t.ApproverName,
        Status = t.Status, Sequence = t.Sequence, Comment = t.Comment, HandledTime = t.HandledTime,
        CreateTime = t.CreateTime, Summary = i.Summary, SubmitterName = i.SubmitterName,
        BusinessTable = i.BusinessTable, BusinessId = i.BusinessId.ToString(), InstanceStatus = i.Status
    };

    private static FlowInstanceDto ToInstanceDto(SysFlowInstance i, IReadOnlyDictionary<string, string> defNames) => new()
    {
        Id = i.Id.ToString(), FlowCode = i.FlowCode,
        FlowName = defNames.GetValueOrDefault(i.DefinitionId.ToString(), i.FlowCode),
        BusinessTable = i.BusinessTable, BusinessId = i.BusinessId.ToString(), Summary = i.Summary,
        CurrentNodeCode = i.CurrentNodeCode, Status = i.Status, SubmitterId = i.SubmitterId.ToString(),
        SubmitterName = i.SubmitterName, CreateTime = i.CreateTime, FinishedTime = i.FinishedTime
    };
}

file static class FlowDefExt
{
    public static string DefinitionKey(this SysFlowDefinition d) => d.Id.ToString();
}
