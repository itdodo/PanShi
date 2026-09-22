using MiniExcelLibs;
using Panshi.Common.Exceptions;
using Panshi.Common.Results;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;

namespace Panshi.Service.Sys;

/// <summary>角色服务（角色管菜单/接口权限；DataScope 数据权限档；admin 角色保护）。</summary>
public class RoleService(
    IRepository<SysRole> roleRepo,
    IRepository<SysRoleMenu> roleMenuRepo,
    IRepository<SysRoleDept> roleDeptRepo,
    IRepository<SysUserRole> userRoleRepo) : BaseService<SysRole>(roleRepo)
{
    public async Task<PagedResult<RoleDto>> PageAsync(RoleQuery query, long actorId)
    {
        var keyword = query.Keyword?.Trim();
        var exp = SqlSugar.Expressionable.Create<SysRole>();
        if (!string.IsNullOrEmpty(keyword)) exp.And(r => r.RoleName.Contains(keyword) || r.RoleCode.Contains(keyword));
        if (query.Status is EnableStatus st) exp.And(r => r.Status == st);

        var (col, desc) = query.ResolveSort(new Dictionary<string, string>
        {
            ["createTime"] = "create_time", ["sort"] = "sort"
        });
        var page = await Repo.PageAsync(exp.ToExpression(), query.PageNum, query.PageSize, col, desc);
        return new PagedResult<RoleDto> { Total = page.Total, Rows = page.Rows.Select(ToDto).ToList() };
    }

    public async Task<List<OptionDto>> ListAsync()
        => (await Repo.ListAsync(r => r.Status == EnableStatus.Enabled))
            .Select(r => new OptionDto { Value = r.Id.ToString(), Label = r.RoleName }).ToList();

    public async Task<RoleDto> GetAsync(long id)
    {
        var role = ToDto(await Repo.GetAsync(id));
        role.MenuIds = (await roleMenuRepo.ListAsync(m => m.RoleId == id)).Select(m => m.MenuId.ToString()).ToList();
        role.DeptIds = (await roleDeptRepo.ListAsync(d => d.RoleId == id)).Select(d => d.DeptId.ToString()).ToList();
        return role;
    }

    public async Task<RoleDto> CreateAsync(RoleSaveDto dto)
    {
        if (await Repo.ExistsAsync(r => r.RoleCode == dto.RoleCode)) throw new BizException("角色编码已存在");
        var role = new SysRole
        {
            RoleCode = dto.RoleCode.Trim(), RoleName = dto.RoleName, DataScope = dto.DataScope,
            Sort = dto.Sort, Status = dto.Status, Remark = dto.Remark
        };
        await Tran.RunAsync(Repo.Db, async () =>
        {
            await Repo.InsertAsync(role);
            await GrantLinksAsync(role.Id, dto.MenuIds, dto.DeptIds);
        });
        PermissionService.InvalidateAll();
        return await GetAsync(role.Id);
    }

    public async Task UpdateAsync(long id, RoleUpdateDto dto, UserAuth actor)
    {
        var role = await Repo.GetAsync(id);
        GuardAdminRole(role, actor);
        if (role.RoleCode != dto.RoleCode && await Repo.ExistsAsync(r => r.RoleCode == dto.RoleCode))
            throw new BizException("角色编码已存在");

        role.RoleCode = dto.RoleCode.Trim();
        role.RoleName = dto.RoleName;
        role.DataScope = dto.DataScope;
        role.Sort = dto.Sort;
        if (role.RoleCode != PermissionService.SuperRoleCode) role.Status = dto.Status;
        role.Remark = dto.Remark;
        role.Version = dto.Version;

        await Tran.RunAsync(Repo.Db, async () =>
        {
            await UpdateWithConcurrencyCheckAsync(role);
            await GrantLinksAsync(id, dto.MenuIds, dto.DeptIds);
        });
        PermissionService.InvalidateAll();
    }

    public async Task DeleteAsync(long id, UserAuth actor)
    {
        var role = await Repo.GetAsync(id);
        GuardAdminRole(role, actor);
        if (await userRoleRepo.ExistsAsync(l => l.RoleId == id))
            throw new BizException("角色已分配用户，不可删除");

        await Tran.RunAsync(Repo.Db, async () =>
        {
            await Repo.SoftDeleteAsync(id);
            await roleMenuRepo.DeleteAsync(m => m.RoleId == id);
            await roleDeptRepo.DeleteAsync(d => d.RoleId == id);
        });
        PermissionService.InvalidateAll();
    }

    /// <summary>授权菜单（保存后权限缓存整体失效）。</summary>
    public async Task GrantMenusAsync(long id, GrantMenusDto dto, UserAuth actor)
    {
        var role = await Repo.GetAsync(id);
        GuardAdminRole(role, actor);
        if (dto.DataScope is not null)
        {
            role.DataScope = dto.DataScope.Value;
            role.Version = role.Version; // 内部列更新不动版本
            await Repo.UpdateColumnsAsync(role, "DataScope");
        }

        await Tran.RunAsync(Repo.Db, async () =>
        {
            await roleMenuRepo.DeleteAsync(m => m.RoleId == id);
            await roleDeptRepo.DeleteAsync(d => d.RoleId == id);
            await GrantLinksAsync(id, dto.MenuIds, dto.DeptIds);
        });
        PermissionService.InvalidateAll();
    }

    public async Task<byte[]> ExportBytesAsync()
    {
        var rows = (await Repo.ListAsync()).Select(r => new
        {
            r.RoleCode, r.RoleName, DataScope = r.DataScope.ToString(), r.Sort, Status = r.Status.ToString(), r.Remark
        }).ToList();
        using var ms = new MemoryStream();
        await ms.SaveAsAsync(rows);
        return ms.ToArray();
    }

    private static void GuardAdminRole(SysRole role, UserAuth actor)
    {
        // 内置 admin 角色的授权结构仅内置管理员本人可动（防护②的写入侧收口）
        if (role.RoleCode == PermissionService.SuperRoleCode && actor.UserName != UserService.AdminName)
            throw new BizException("内置 admin 角色仅限管理员本人维护");
    }

    private async Task GrantLinksAsync(long roleId, List<long> menuIds, List<long> deptIds)
    {
        if (menuIds.Count > 0)
            await roleMenuRepo.InsertRangeAsync(menuIds.Distinct()
                .Select(mid => new SysRoleMenu { RoleId = roleId, MenuId = mid }).ToList());
        if (deptIds.Count > 0)
            await roleDeptRepo.InsertRangeAsync(deptIds.Distinct()
                .Select(did => new SysRoleDept { RoleId = roleId, DeptId = did }).ToList());
    }

    private static RoleDto ToDto(SysRole r) => new()
    {
        Id = r.Id.ToString(), RoleCode = r.RoleCode, RoleName = r.RoleName, DataScope = r.DataScope,
        Sort = r.Sort, Status = r.Status, Remark = r.Remark, CreateTime = r.CreateTime,
        Version = r.Version // 红线 #6
    };
}
