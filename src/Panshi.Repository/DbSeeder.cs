using Panshi.Common.Security;
using Panshi.Model.Entities;
using Panshi.Model.Enums;
using SqlSugar;

namespace Panshi.Repository;

/// <summary>
/// 内置数据种子（幂等：分组存在即跳过）。
/// ⚠️ 红线 #8：直连 Insertable 不触发雪花 AOP——种子使用固定小整数 Id（业务表雪花 Id 为 17 位大数，不会冲突），
///   父子引用（菜单树/用户角色）全部显式声明。
/// ⚠️ 红线 #14：菜单幂等哨兵码 "__seed_v1__" 独立于任何页面权限码。
/// ⚠️ 红线 #18：每条可见菜单必须带 iconify 图标名（lucide 图标集）。
/// </summary>
public static class DbSeeder
{
    public static void Seed(ISqlSugarClient db)
    {
        SeedConfigs(db);
        SeedDicts(db);
        SeedOrg(db);
        SeedMenus(db);
        SeedRoleMenus(db);
    }

    private static readonly DateTime Now = DateTime.Now;

    // ---------- 内置参数 ----------
    private static void SeedConfigs(ISqlSugarClient db)
    {
        if (db.Queryable<SysConfig>().Any(c => c.ConfigKey == "sys.user.initPassword")) return;

        var rows = new List<SysConfig>();
        void Cfg(long id, string name, string key, string val, string remark) => rows.Add(new SysConfig
        {
            Id = id, ConfigName = name, ConfigKey = key, ConfigValue = val, BuiltIn = true, Remark = remark,
            CreateTime = Now
        });
        Cfg(1, "初始密码", "sys.user.initPassword", "Abc@123456", "新建用户/重置密码的默认值");
        Cfg(2, "登录失败锁定阈值", "sys.login.failLimit", "5", "连续失败达到次数锁定账号");
        Cfg(3, "账号锁定时长(分钟)", "sys.login.lockMinutes", "15", "达到阈值后的锁定时长");
        Cfg(4, "登录验证码开关", "sys.captcha.enabled", "1", "1开启 0关闭");
        Cfg(5, "密码有效期(天)", "sys.pwd.expireDays", "90", "0=不限期；超期登录强制改密");
        Cfg(6, "同账号互踢", "sys.login.kickSameUser", "1", "1开启：新登录挤掉旧会话并强下线");
        db.Insertable(rows).ExecuteCommand();
    }

    // ---------- 内置字典 10 组 ----------
    private static void SeedDicts(ISqlSugarClient db)
    {
        if (db.Queryable<SysDictType>().Any(t => t.DictCode == "sys_normal_disable")) return;

        var types = new List<SysDictType>();
        var data = new List<SysDictData>();
        long typeId = 100;
        long dataId = 1000;

        void Dict(string code, string name, params (string label, string value, string? tag)[] items)
        {
            types.Add(new SysDictType { Id = typeId, DictCode = code, DictName = name, CreateTime = Now });
            var sort = 1;
            foreach (var (label, value, tag) in items)
                data.Add(new SysDictData
                {
                    Id = dataId++, DictTypeId = typeId, Label = label, Value = value, Sort = sort++,
                    TagType = tag, Status = 0, CreateTime = Now
                });
            typeId++;
        }

        Dict("sys_normal_disable", "启用状态", ("正常", "0", "success"), ("停用", "1", "danger"));
        Dict("sys_yes_no", "是/否", ("是", "1", "success"), ("否", "0", "info"));
        Dict("sys_show_hide", "菜单显隐", ("显示", "1", "success"), ("隐藏", "0", "info"));
        Dict("sys_menu_type", "菜单类型", ("目录", "1", "info"), ("菜单", "2", "success"), ("按钮", "3", "warning"));
        Dict("sys_data_scope", "数据权限", ("全部数据", "1", "danger"), ("本部门", "2", "info"),
            ("本部门及以下", "3", "warning"), ("仅本人", "4", "success"), ("自定义", "5", "info"));
        Dict("sys_notice_type", "公告类型", ("通知", "1", "info"), ("公告", "2", "warning"));
        Dict("sys_msg_type", "消息类型", ("系统", "1", "info"), ("站内信", "2", "success"), ("业务", "3", "warning"));
        Dict("sys_flow_status", "审批实例状态", ("审批中", "1", "warning"), ("通过", "2", "success"),
            ("拒绝", "3", "danger"), ("撤回", "4", "info"), ("作废", "5", "danger"));
        Dict("sys_doc_status", "单据状态", ("草稿", "0", "info"), ("审批中", "1", "warning"),
            ("通过", "2", "success"), ("拒绝", "3", "danger"), ("撤回", "4", "info"));
        Dict("sys_expense_category", "报销类别", ("差旅", "travel", "info"), ("办公用品", "office", "success"),
            ("业务招待", "hospitality", "warning"), ("通讯", "telecom", "info"), ("其他", "other", "default"));

        db.Insertable(types).ExecuteCommand();
        db.Insertable(data).ExecuteCommand();
    }

    // ---------- 部门/岗位/角色/admin ----------
    private static void SeedOrg(ISqlSugarClient db)
    {
        if (db.Queryable<SysUser>().Any(u => u.UserName == "admin")) return;

        db.Insertable(new List<SysDept>
        {
            New(1, null, "HQ", "总公司", null, 0),
            New(2, 1, "HQ-GM", "总经办", null, 1),
            New(3, 1, "HQ-FIN", "财务部", null, 2),
            New(4, 1, "HQ-PUR", "采购部", null, 3),
            New(5, 1, "HQ-HR", "行政人事部", null, 4),
        }.ToList()).ExecuteCommand();

        static SysDept New(long id, long? parent, string code, string name, string? leader, int sort) => new()
        {
            Id = id, ParentId = parent, DeptCode = code, DeptName = name, Leader = leader, Sort = sort,
            Status = 0, Ancestors = parent is null ? "0" : $"0,{parent}", CreateTime = Now
        };

        db.Insertable(new List<SysPosition>
        {
            Pos(11, "ceo", "总经理", 1), Pos(12, "finance_manager", "财务经理", 2),
            Pos(13, "purchase_manager", "采购经理", 3), Pos(14, "hr_manager", "人事经理", 4),
            Pos(15, "staff", "普通员工", 9)
        }).ExecuteCommand();

        static SysPosition Pos(long id, string code, string name, int sort) => new()
        {
            Id = id, PositionCode = code, PositionName = name, Sort = sort, Status = 0, CreateTime = Now
        };

        db.Insertable(new List<SysRole>
        {
            Role(20, "admin", "超级管理员", DataScopeType.All, 1),
            Role(21, "manager", "部门主管", DataScopeType.DeptAndChild, 2),
            Role(22, "staff", "普通员工", DataScopeType.Self, 3)
        }).ExecuteCommand();

        static SysRole Role(long id, string code, string name, DataScopeType scope, int sort) => new()
        {
            Id = id, RoleCode = code, RoleName = name, DataScope = scope, Sort = sort,
            Status = EnableStatus.Enabled, CreateTime = Now
        };

        db.Insertable(new SysUser
        {
            Id = 1, UserName = "admin", Password = PasswordHasher.Hash("Admin@123456"),
            NickName = "超级管理员", DeptId = 2, Status = EnableStatus.Enabled,
            PwdUpdateTime = Now, OwnerUserId = 1, Remark = "内置管理员（防提权四防护的唯一授权人）",
            CreateTime = Now
        }).ExecuteCommand();

        db.Insertable(new SysUserRole { Id = 30, UserId = 1, RoleId = 20, CreateTime = Now }).ExecuteCommand();
        db.Insertable(new SysUserPosition { Id = 31, UserId = 1, PositionId = 11, CreateTime = Now }).ExecuteCommand();
    }

    // ---------- 菜单树（icon=lucide；哨兵幂等） ----------
    private static void SeedMenus(ISqlSugarClient db)
    {
        if (db.Queryable<SysMenu>().Any(m => m.Permission == "__seed_v1__")) return;

        var menus = new List<SysMenu>();

        void Dir(long id, string name, string icon, int sort) => menus.Add(new SysMenu
        {
            Id = id, ParentId = null, MenuName = name, MenuType = MenuType.Directory, Icon = icon,
            Sort = sort, Visible = true, Status = EnableStatus.Enabled, CreateTime = Now
        });

        void Page(long id, long? parent, string name, string path, string component, string? perm, string icon,
            int sort, bool visible = true) => menus.Add(new SysMenu
        {
            Id = id, ParentId = parent, MenuName = name, MenuType = MenuType.Menu, Path = path,
            Component = component, Permission = perm, Icon = icon, Sort = sort, Visible = visible,
            Status = EnableStatus.Enabled, CreateTime = Now
        });

        void Btn(long id, long parent, string name, string perm) => menus.Add(new SysMenu
        {
            Id = id, ParentId = parent, MenuName = name, MenuType = MenuType.Button, Permission = perm,
            Sort = 0, Visible = false, Status = EnableStatus.Enabled, CreateTime = Now
        });

        Page(1, null, "仪表盘", "/dashboard", "dashboard/index", "dashboard:view", "lucide:layout-dashboard", 1);

        Dir(2, "系统管理", "lucide:settings", 2);
        Page(201, 2, "用户管理", "/system/user", "system/user/index", "sys:user:list", "lucide:users", 1);
        Btn(2011, 201, "新增", "sys:user:add");
        Btn(2012, 201, "编辑", "sys:user:edit");
        Btn(2013, 201, "删除", "sys:user:delete");
        Btn(2014, 201, "重置密码", "sys:user:resetpwd");
        Btn(2015, 201, "导入", "sys:user:import");
        Btn(2016, 201, "导出", "sys:user:export");
        Page(202, 2, "角色管理", "/system/role", "system/role/index", "sys:role:list", "lucide:shield", 2);
        Btn(2021, 202, "新增", "sys:role:add");
        Btn(2022, 202, "编辑", "sys:role:edit");
        Btn(2023, 202, "删除", "sys:role:delete");
        Page(203, 2, "菜单管理", "/system/menu", "system/menu/index", "sys:menu:list", "lucide:list-tree", 3);
        Btn(2031, 203, "新增", "sys:menu:add");
        Btn(2032, 203, "编辑", "sys:menu:edit");
        Btn(2033, 203, "删除", "sys:menu:delete");
        Page(204, 2, "部门管理", "/system/dept", "system/dept/index", "sys:dept:list", "lucide:building-2", 4);
        Btn(2041, 204, "新增", "sys:dept:add");
        Btn(2042, 204, "编辑", "sys:dept:edit");
        Btn(2043, 204, "删除", "sys:dept:delete");
        Page(205, 2, "岗位管理", "/system/position", "system/position/index", "sys:position:list",
            "lucide:briefcase", 5);
        Btn(2051, 205, "新增", "sys:position:add");
        Btn(2052, 205, "编辑", "sys:position:edit");
        Btn(2053, 205, "删除", "sys:position:delete");
        Page(206, 2, "字典管理", "/system/dict", "system/dict/index", "sys:dict:list", "lucide:book-open", 6);
        Btn(2061, 206, "新增", "sys:dict:add");
        Btn(2062, 206, "编辑", "sys:dict:edit");
        Btn(2063, 206, "删除", "sys:dict:delete");
        Page(207, 2, "参数设置", "/system/config", "system/config/index", "sys:config:list",
            "lucide:sliders-horizontal", 7);
        Btn(2071, 207, "新增", "sys:config:add");
        Btn(2072, 207, "编辑", "sys:config:edit");
        Btn(2073, 207, "删除", "sys:config:delete");
        Page(208, 2, "公告管理", "/system/notice", "system/notice/index", "sys:notice:list", "lucide:megaphone", 8);
        Btn(2081, 208, "新增", "sys:notice:add");
        Btn(2082, 208, "编辑", "sys:notice:edit");
        Btn(2083, 208, "删除", "sys:notice:delete");
        Page(209, 2, "消息推送", "/system/message", "system/message/index", "sys:message:list",
            "lucide:message-square", 9);
        Btn(2091, 209, "发送", "sys:message:send");
        Page(210, 2, "个人中心", "/profile", "system/profile/index", null, "lucide:user-circle", 99, visible: false);

        Dir(3, "审批中心", "lucide:git-pull-request", 3);
        Page(301, 3, "我的待办", "/flow/todo", "flow/todo/index", "workflow:task:todo", "lucide:inbox", 1);
        Page(302, 3, "我的已办", "/flow/done", "flow/done/index", "workflow:task:done", "lucide:badge-check", 2);
        Page(303, 3, "我发起的", "/flow/mine", "flow/mine/index", "workflow:instance:my", "lucide:send", 3);
        Page(304, 3, "抄送我的", "/flow/cc", "flow/cc/index", "workflow:cc:me", "lucide:mail", 4);
        Page(305, 3, "流程定义", "/flow/def", "flow/def/index", "workflow:def:list", "lucide:workflow", 5);
        Btn(3051, 305, "新增", "workflow:def:add");
        Btn(3052, 305, "编辑", "workflow:def:edit");
        Btn(3053, 305, "删除", "workflow:def:delete");
        Btn(3054, 305, "启停", "workflow:def:enable");
        Page(306, 3, "流程实例", "/flow/instance", "flow/instance/index", "workflow:instance:list",
            "lucide:files", 6);
        Btn(3061, 306, "作废", "workflow:instance:void");
        Page(307, 3, "单据绑定", "/flow/binding", "flow/binding/index", "workflow:binding:list", "lucide:link", 7);
        Btn(3071, 307, "换绑/停用", "workflow:binding:edit");

        Dir(4, "业务模块", "lucide:folder-kanban", 4);
        Page(401, 4, "报销单", "/biz/expense", "biz/expense/index", "biz:expense:list", "lucide:receipt", 1);
        Btn(4011, 401, "新增", "biz:expense:add");
        Btn(4012, 401, "编辑", "biz:expense:edit");
        Btn(4013, 401, "删除", "biz:expense:delete");
        Btn(4014, 401, "提交审批", "biz:expense:submit");
        Page(402, 4, "采购申请单", "/biz/purchase", "biz/purchase/index", "biz:purchase:list",
            "lucide:shopping-cart", 2);
        Btn(4021, 402, "新增", "biz:purchase:add");
        Btn(4022, 402, "编辑", "biz:purchase:edit");
        Btn(4023, 402, "删除", "biz:purchase:delete");
        Btn(4024, 402, "提交审批", "biz:purchase:submit");

        Dir(5, "日志审计", "lucide:scroll-text", 5);
        Page(501, 5, "操作日志", "/monitor/operlog", "monitor/operlog/index", "monitor:operlog:list",
            "lucide:activity", 1);
        Btn(5011, 501, "导出", "monitor:operlog:export");
        Btn(5012, 501, "清理", "monitor:operlog:clean");
        Page(502, 5, "登录日志", "/monitor/loginlog", "monitor/loginlog/index", "monitor:loginlog:list",
            "lucide:log-in", 2);
        Btn(5021, 502, "导出", "monitor:loginlog:export");
        Btn(5022, 502, "清理", "monitor:loginlog:clean");
        Page(503, 5, "变更日志", "/monitor/changelog", "monitor/changelog/index", "monitor:changelog:list",
            "lucide:git-compare", 3);
        Btn(5031, 503, "清理", "monitor:changelog:clean");

        Dir(6, "系统监控", "lucide:monitor", 6);
        Page(601, 6, "在线用户", "/monitor/online", "monitor/online/index", "monitor:online:list",
            "lucide:wifi", 1);
        Btn(6011, 601, "强退", "monitor:online:kick");
        Page(602, 6, "定时任务", "/monitor/job", "monitor/job/index", "monitor:job:list", "lucide:timer", 2);
        Btn(6021, 602, "管理", "monitor:job:manage");
        Page(604, 6, "IP 黑白名单", "/monitor/ipguard", "monitor/ipguard/index", "monitor:ipguard:list",
            "lucide:shield", 4);
        Btn(6041, 604, "管理", "monitor:ipguard:manage");
        Page(603, 6, "服务监控", "/monitor/server", "monitor/server/index", "monitor:server:list",
            "lucide:gauge", 3);

        // 幂等哨兵（非页面权限码，红线 #14）
        Btn(9999, 2, "SEED_V1", "__seed_v1__");

        db.Insertable(menus).ExecuteCommand();
    }

    /// <summary>角色↔菜单：admin 全量；manager/staff 常用集（演示数据权限与审批轮转）。</summary>
    private static void SeedRoleMenus(ISqlSugarClient db)
    {
        if (db.Queryable<SysRoleMenu>().Any(rm => rm.RoleId == 20)) return;

        var all = db.Queryable<SysMenu>().ToList().Select(m => m.Id).ToList();
        var rows = all.Select(menuId => new SysRoleMenu { Id = SnowflakeId.NextId(), RoleId = 20, MenuId = menuId, CreateTime = Now }).ToList();

        long[] common = [1, 3, 301, 302, 303, 304, 4, 401, 4011, 4012, 4013, 4014, 402, 4021, 4022, 4023, 4024,
            2, 210];
        foreach (var roleId in new long[] { 21, 22 })
        foreach (var menuId in common)
            rows.Add(new SysRoleMenu { Id = SnowflakeId.NextId(), RoleId = roleId, MenuId = menuId, CreateTime = Now });

        db.Insertable(rows).ExecuteCommand();
    }
}
