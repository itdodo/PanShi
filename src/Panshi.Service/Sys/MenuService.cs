using Panshi.Common.Exceptions;
using Panshi.Model.Dtos;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using Panshi.Repository;
using Panshi.Service.Base;

namespace Panshi.Service.Sys;

/// <summary>
/// 菜单服务。三接口分权（蓝图§5.4）：
/// 全量树=sys:menu:list（控制器管）；tree/my 登录即可——管理员全量 / 普通用户角色并集+父级链补全 / 无角色空树。
/// </summary>
public class MenuService(
    IRepository<SysMenu> menuRepo,
    IRepository<SysRoleMenu> roleMenuRepo,
    IRepository<SysUserRole> userRoleRepo,
    PermissionService permissions) : BaseService<SysMenu>(menuRepo)
{
    public async Task<List<MenuDto>> FullTreeAsync()
        => BuildTree((await Repo.ListAsync()).OrderBy(m => m.Sort).ThenBy(m => m.Id).Select(ToDto).ToList());

    /// <summary>我的菜单树（仅目录+菜单，按钮不进树；隐藏项保留 visible 由前端决定）。</summary>
    public async Task<List<MenuDto>> MyTreeAsync(long userId)
    {
        var auth = await permissions.GetAuthAsync(userId) ?? throw BizException.Unauthorized();
        var all = (await Repo.ListAsync(m => m.Status == EnableStatus.Enabled && m.MenuType != MenuType.Button))
            .OrderBy(m => m.Sort).ThenBy(m => m.Id).ToList();

        if (!auth.IsSuper)
        {
            var roleIds = (await userRoleRepo.ListAsync(l => l.UserId == userId)).Select(l => l.RoleId).ToHashSet();
            if (roleIds.Count == 0) return []; // 无角色=空树 → 前端 403 友好页
            var menuIds = (await roleMenuRepo.ListAsync(rm => roleIds.Contains(rm.RoleId)))
                .Select(rm => rm.MenuId).ToHashSet();
            var kept = all.Where(m => menuIds.Contains(m.Id)).ToHashSet();
            // 父级链补全
            var byId = all.ToDictionary(m => m.Id);
            foreach (var m in kept.ToList())
            {
                var parent = m.ParentId;
                while (parent is long pid && byId.TryGetValue(pid, out var p) && kept.Add(p) && p.ParentId != null)
                    parent = p.ParentId;
            }

            all = all.Where(m => kept.Contains(m)).ToList();
        }

        return BuildTree(all.Select(ToDto).ToList());
    }

    public async Task<MenuDto> CreateAsync(MenuSaveDto dto)
    {
        if (await Repo.ExistsAsync(m => m.MenuName == dto.MenuName && m.ParentId == dto.ParentId))
            throw new BizException("同级下菜单名已存在");
        if (dto.MenuType == MenuType.Button && string.IsNullOrWhiteSpace(dto.Permission))
            throw new BizException("按钮必须填权限码");
        if (!string.IsNullOrWhiteSpace(dto.Permission) &&
            await Repo.ExistsAsync(m => m.Permission == dto.Permission && m.MenuType == MenuType.Button))
            throw new BizException("权限码已存在");

        var menu = Apply(new SysMenu(), dto);
        await Repo.InsertAsync(menu);
        PermissionService.InvalidateAll();
        return ToDto(menu);
    }

    public async Task UpdateAsync(long id, MenuSaveDto dto)
    {
        var menu = await Repo.GetAsync(id);
        if (menu.Id == dto.ParentId) throw new BizException("父级不能是自己");
        Apply(menu, dto);
        menu.Version = dto.Version;
        await UpdateWithConcurrencyCheckAsync(menu);
        PermissionService.InvalidateAll();
    }

    public async Task DeleteAsync(long id)
    {
        if (await Repo.ExistsAsync(m => m.ParentId == id)) throw new BizException("存在子菜单，不可删除");
        await Repo.SoftDeleteAsync(id);
        await roleMenuRepo.DeleteAsync(rm => rm.MenuId == id);
        PermissionService.InvalidateAll();
    }

    /// <summary>父级下拉树（编辑用）。</summary>
    public async Task<List<MenuOptionDto>> OptionsAsync()
        => BuildOptions((await Repo.ListAsync(m => m.MenuType != MenuType.Button))
            .OrderBy(m => m.Sort).ToList(), null);

    private static SysMenu Apply(SysMenu menu, MenuSaveDto dto)
    {
        menu.ParentId = dto.ParentId;
        menu.MenuName = dto.MenuName;
        menu.MenuType = dto.MenuType;
        menu.Path = dto.Path;
        menu.Component = dto.Component;
        menu.Permission = dto.Permission?.Trim();
        menu.Icon = dto.Icon;
        menu.Visible = dto.Visible;
        menu.Status = dto.Status;
        menu.Sort = dto.Sort;
        return menu;
    }

    private static List<MenuDto> BuildTree(List<MenuDto> flat)
    {
        var byId = flat.ToDictionary(m => m.Id);
        var roots = new List<MenuDto>();
        foreach (var m in flat)
        {
            if (m.ParentId is string pid && byId.TryGetValue(pid, out var parent)) parent.Children.Add(m);
            else roots.Add(m);
        }

        return roots;
    }

    private static List<MenuOptionDto> BuildOptions(List<SysMenu> all, long? parent)
        => all.Where(m => m.ParentId == parent).OrderBy(m => m.Sort)
            .Select(m => new MenuOptionDto
            {
                Value = m.Id.ToString(), Label = m.MenuName, Children = BuildOptions(all, m.Id)
            }).ToList();

    private static MenuDto ToDto(SysMenu m) => new()
    {
        Id = m.Id.ToString(), ParentId = m.ParentId?.ToString(), MenuName = m.MenuName,
        MenuType = m.MenuType, Path = m.Path, Component = m.Component, Permission = m.Permission,
        Icon = m.Icon, Visible = m.Visible, Status = m.Status, Sort = m.Sort, Version = m.Version
    };
}
