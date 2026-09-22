using System.ComponentModel.DataAnnotations;
using Panshi.Model.Enums;

namespace Panshi.Model.Dtos;

// ---------------- 角色 ----------------
public class RoleQuery : PagedQuery
{
    public string? Keyword { get; set; }

    public EnableStatus? Status { get; set; }
}

public class RoleDto
{
    public string Id { get; set; } = "";

    public string RoleCode { get; set; } = "";

    public string RoleName { get; set; } = "";

    public DataScopeType DataScope { get; set; }

    public int Sort { get; set; }

    public EnableStatus Status { get; set; }

    public string? Remark { get; set; }

    public List<string> MenuIds { get; set; } = [];

    public List<string> DeptIds { get; set; } = [];

    public DateTime CreateTime { get; set; }

    public int Version { get; set; }
}

public class RoleSaveDto
{
    [Required, StringLength(64, MinimumLength = 2)]
    public string RoleCode { get; set; } = "";

    [Required, StringLength(64)]
    public string RoleName { get; set; } = "";

    public DataScopeType DataScope { get; set; } = DataScopeType.Self;

    public int Sort { get; set; }

    public EnableStatus Status { get; set; }

    public string? Remark { get; set; }

    public List<long> MenuIds { get; set; } = [];

    public List<long> DeptIds { get; set; } = [];
}

public class RoleUpdateDto : RoleSaveDto
{
    /// <summary>乐观锁（必传回显值——红线 #6）</summary>
    public int Version { get; set; }
}

/// <summary>角色授权菜单</summary>
public class GrantMenusDto
{
    public List<long> MenuIds { get; set; } = [];

    public DataScopeType? DataScope { get; set; }

    public List<long> DeptIds { get; set; } = [];
}

// ---------------- 菜单 ----------------
public class MenuDto
{
    public string Id { get; set; } = "";

    public string? ParentId { get; set; }

    public string MenuName { get; set; } = "";

    public MenuType MenuType { get; set; }

    public string? Path { get; set; }

    public string? Component { get; set; }

    public string? Permission { get; set; }

    public string? Icon { get; set; }

    public bool Visible { get; set; } = true;

    public EnableStatus Status { get; set; }

    public int Sort { get; set; }

    /// <summary>乐观锁（编辑回传）</summary>
    public int Version { get; set; }

    public List<MenuDto> Children { get; set; } = [];
}

public class MenuSaveDto
{
    public long? ParentId { get; set; }

    [Required, StringLength(64)]
    public string MenuName { get; set; } = "";

    public MenuType MenuType { get; set; } = MenuType.Menu;

    [StringLength(256)]
    public string? Path { get; set; }

    [StringLength(256)]
    public string? Component { get; set; }

    [StringLength(128)]
    public string? Permission { get; set; }

    /// <summary>iconify 图标名（lucide:xxx；目录/菜单必填，红线 #18）</summary>
    [StringLength(128)]
    public string? Icon { get; set; }

    public bool Visible { get; set; } = true;

    public EnableStatus Status { get; set; }

    public int Sort { get; set; }

    public int Version { get; set; }
}

/// <summary>菜单下拉选项（父级选择）</summary>
public class MenuOptionDto
{
    public string Value { get; set; } = "";

    public string Label { get; set; } = "";

    public List<MenuOptionDto> Children { get; set; } = [];
}
