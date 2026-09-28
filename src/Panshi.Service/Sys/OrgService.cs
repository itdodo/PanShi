using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;
using SqlSugar;

namespace Panshi.Service.Sys;

/// <summary>部门服务（树；Ancestors 祖级链维护；有子/有人禁删）。</summary>
public class DeptService(IRepository<SysDept> deptRepo, IRepository<SysUser> userRepo) : BaseService<SysDept>(deptRepo)
{
    public async Task<List<DeptDto>> TreeAsync(string? keyword = null)
    {
        var all = (await Repo.ListAsync()).OrderBy(d => d.Sort).ThenBy(d => d.Id).ToList();
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var kw = keyword.Trim();
            var kept = all.Where(d => d.DeptName.Contains(kw) || d.DeptCode.Contains(kw))
                .Select(d => d.Id).ToHashSet();
            // 必须把命中节点的整条祖级链补回来：BuildTree 以 ParentId==null 为根递归，
            // 只留命中行的话，深层节点会因为父级被筛掉而整棵消失（搜「财务」返回空数组）。
            var byId = all.ToDictionary(d => d.Id);
            foreach (var id in kept.ToList())
            {
                var parent = byId[id].ParentId;
                while (parent is long pid && byId.ContainsKey(pid) && kept.Add(pid))
                    parent = byId[pid].ParentId;
            }

            all = all.Where(d => kept.Contains(d.Id)).ToList();
        }

        return BuildTree(all, null);
    }

    public async Task<List<OptionDto>> OptionsAsync()
        => (await Repo.ListAsync(d => d.Status == 0)).Select(d => new OptionDto
        {
            Value = d.Id.ToString(), Label = d.DeptName
        }).ToList();

    public async Task<DeptDto> CreateAsync(DeptSaveDto dto)
    {
        if (await Repo.ExistsAsync(d => d.DeptCode == dto.DeptCode)) throw new BizException("部门编码已存在");
        var dept = new SysDept
        {
            ParentId = dto.ParentId, DeptCode = dto.DeptCode.Trim(), DeptName = dto.DeptName,
            Leader = dto.Leader, LeaderUserId = dto.LeaderUserId, Sort = dto.Sort, Status = dto.Status,
            Ancestors = await BuildAncestorsAsync(dto.ParentId)
        };
        await Repo.InsertAsync(dept);
        return ToDto(dept);
    }

    public async Task UpdateAsync(long id, DeptSaveDto dto)
    {
        var dept = await Repo.GetAsync(id);
        if (dept.DeptCode != dto.DeptCode && await Repo.ExistsAsync(d => d.DeptCode == dto.DeptCode))
            throw new BizException("部门编码已存在");
        if (dept.Id == dto.ParentId) throw new BizException("上级不能是自己");

        dept.ParentId = dto.ParentId;
        dept.DeptCode = dto.DeptCode.Trim();
        dept.DeptName = dto.DeptName;
        dept.Leader = dto.Leader;
        dept.LeaderUserId = dto.LeaderUserId;
        dept.Sort = dto.Sort;
        dept.Status = dto.Status;
        dept.Ancestors = await BuildAncestorsAsync(dto.ParentId);
        dept.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(dept);
        await RefreshChildrenAncestorsAsync(dept);
    }

    public async Task DeleteAsync(long id)
    {
        if (await Repo.ExistsAsync(d => d.ParentId == id)) throw new BizException("存在下级部门，不可删除");
        if (await userRepo.ExistsAsync(u => u.DeptId == id)) throw new BizException("部门下存在用户，不可删除");
        await Repo.SoftDeleteAsync(id);
    }

    private async Task<string> BuildAncestorsAsync(long? parentId)
    {
        if (parentId is not long pid) return "0";
        var parent = await Repo.FindAsync(pid);
        return $"{parent?.Ancestors ?? "0"},{pid}";
    }

    private async Task RefreshChildrenAncestorsAsync(SysDept dept)
    {
        var all = await Repo.ListAsync();
        var frontier = new List<SysDept> { dept };
        var byParent = all.GroupBy(d => d.ParentId ?? 0).ToDictionary(g => g.Key, g => g.ToList());
        while (frontier.Count > 0)
        {
            var next = new List<SysDept>();
            foreach (var cur in frontier)
            {
                if (!byParent.TryGetValue(cur.Id, out var children)) continue;
                foreach (var child in children)
                {
                    child.Ancestors = $"{cur.Ancestors},{cur.Id}";
                    await Repo.UpdateColumnsAsync(child, "Ancestors");
                    next.Add(child);
                }
            }

            frontier = next;
        }
    }

    private static DeptDto ToDto(SysDept d) => new()
    {
        Id = d.Id.ToString(), ParentId = d.ParentId?.ToString(), DeptCode = d.DeptCode, DeptName = d.DeptName,
        Leader = d.Leader, LeaderUserId = d.LeaderUserId?.ToString(), Sort = d.Sort, Status = d.Status,
        CreateTime = d.CreateTime, Version = d.Version
    };

    private static List<DeptDto> BuildTree(List<SysDept> all, long? parent)
        => all.Where(d => d.ParentId == parent).OrderBy(d => d.Sort)
            .Select(d => new DeptDto
            {
                Id = d.Id.ToString(), ParentId = d.ParentId?.ToString(), DeptCode = d.DeptCode,
                DeptName = d.DeptName, Leader = d.Leader, LeaderUserId = d.LeaderUserId?.ToString(),
                Sort = d.Sort, Status = d.Status, CreateTime = d.CreateTime, Version = d.Version,
                Children = BuildTree(all, d.Id)
            }).ToList();
}

/// <summary>岗位服务（审批权限载体）。</summary>
public class PositionService(IRepository<SysPosition> repo) : BaseService<SysPosition>(repo)
{
    public async Task<PagedResult<PositionDto>> PageAsync(PositionQuery query)
    {
        var keyword = query.Keyword?.Trim();
        var exp = Expressionable.Create<SysPosition>();
        if (!string.IsNullOrEmpty(keyword)) exp.And(p => p.PositionName.Contains(keyword) || p.PositionCode.Contains(keyword));
        if (query.Status is int st) exp.And(p => p.Status == st);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string>
        {
            ["createTime"] = "create_time", ["sort"] = "sort"
        });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<PositionDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    public async Task<List<OptionDto>> ListAsync()
        => (await Repo.ListAsync(p => p.Status == 0)).OrderBy(p => p.Sort)
            .Select(p => new OptionDto { Value = p.Id.ToString(), Label = p.PositionName }).ToList();

    public async Task<PositionDto> CreateAsync(PositionSaveDto dto)
    {
        if (await Repo.ExistsAsync(p => p.PositionCode == dto.PositionCode)) throw new BizException("岗位编码已存在");
        var p = new SysPosition
        {
            PositionCode = dto.PositionCode.Trim(), PositionName = dto.PositionName, Sort = dto.Sort,
            Status = dto.Status, Remark = dto.Remark
        };
        await Repo.InsertAsync(p);
        return ToDto(p);
    }

    public async Task UpdateAsync(long id, PositionSaveDto dto)
    {
        var p = await Repo.GetAsync(id);
        if (p.PositionCode != dto.PositionCode && await Repo.ExistsAsync(x => x.PositionCode == dto.PositionCode))
            throw new BizException("岗位编码已存在");
        p.PositionCode = dto.PositionCode.Trim();
        p.PositionName = dto.PositionName;
        p.Sort = dto.Sort;
        p.Status = dto.Status;
        p.Remark = dto.Remark;
        p.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(p);
    }

    public async Task DeleteAsync(long id)
    {
        await Repo.SoftDeleteAsync(id);
    }

    private static PositionDto ToDto(SysPosition p) => new()
    {
        Id = p.Id.ToString(), PositionCode = p.PositionCode, PositionName = p.PositionName, Sort = p.Sort,
        Status = p.Status, Remark = p.Remark, CreateTime = p.CreateTime, Version = p.Version
    };
}
