using Panshi.Model.Enums;
using SqlSugar;

namespace Panshi.Model.Entities;

/// <summary>角色（角色管菜单/接口权限；DataScope 决定数据权限档）</summary>
[SugarTable("sys_role")]
public class SysRole : BaseEntity
{
    /// <summary>角色编码（唯一；admin=内置管理员，迁移脚本建过滤唯一索引）</summary>
    [SugarColumn(Length = 64)]
    public string RoleCode { get; set; } = "";

    [SugarColumn(Length = 64)]
    public string RoleName { get; set; } = "";

    /// <summary>数据权限五档</summary>
    public DataScopeType DataScope { get; set; } = DataScopeType.Self;

    public int Sort { get; set; }

    public EnableStatus Status { get; set; }

    [SugarColumn(IsNullable = true, Length = 512)]
    public string? Remark { get; set; }
}

/// <summary>菜单/按钮（树；Icon=iconify 图标名，如 lucide:settings）</summary>
[SugarTable("sys_menu")]
public class SysMenu : BaseEntity
{
    [SugarColumn(IsNullable = true, ColumnDataType = "bigint")]
    public long? ParentId { get; set; }

    [SugarColumn(Length = 64)]
    public string MenuName { get; set; } = "";

    public MenuType MenuType { get; set; } = MenuType.Menu;

    /// <summary>路由路径（menuType=2）</summary>
    [SugarColumn(IsNullable = true, Length = 256)]
    public string? Path { get; set; }

    /// <summary>前端组件路径（views 相对路径，如 system/user/index）</summary>
    [SugarColumn(IsNullable = true, Length = 256)]
    public string? Component { get; set; }

    /// <summary>权限码（如 sys:user:add；按钮型仅作权限码载体）</summary>
    [SugarColumn(IsNullable = true, Length = 128)]
    public string? Permission { get; set; }

    /// <summary>iconify 图标名（种子菜单必须带图标，红线 #18）</summary>
    [SugarColumn(IsNullable = true, Length = 128)]
    public string? Icon { get; set; }

    /// <summary>是否显示</summary>
    public bool Visible { get; set; } = true;

    public EnableStatus Status { get; set; }

    public int Sort { get; set; }
}

/// <summary>角色↔菜单（接口/按钮权限）</summary>
[SugarTable("sys_role_menu")]
public class SysRoleMenu : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long RoleId { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long MenuId { get; set; }
}

/// <summary>角色↔部门（DataScope=Custom 时的数据范围）</summary>
[SugarTable("sys_role_dept")]
public class SysRoleDept : BaseEntity
{
    [SugarColumn(ColumnDataType = "bigint")]
    public long RoleId { get; set; }

    [SugarColumn(ColumnDataType = "bigint")]
    public long DeptId { get; set; }
}
