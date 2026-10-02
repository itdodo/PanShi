-- 0009：进销存汇总报表的菜单与权限码
-- ⚠️ 幂等：WHERE NOT EXISTS + ON CONFLICT (id) DO NOTHING；历史迁移禁改。
-- 新菜单只写这里、不写进 DbSeeder（0003 的教训：两边都写同一批固定 ID，全新库会撞 sys_menu_pkey）。

INSERT INTO sys_menu (id, parent_id, menu_name, menu_type, path, component, permission, icon, visible, status,
                      sort, create_time, is_deleted, version)
VALUES
    (807, 8, '进销存汇总', 2, '/scm/stock-summary', 'scm/stock-summary/index', 'scm:summary:list',
            'lucide:chart-no-axes-combined', true, 0, 7, now(), false, 0)
ON CONFLICT (id) DO NOTHING;

INSERT INTO sys_role_menu (id, role_id, menu_id, create_time, is_deleted, version)
SELECT 700000000000000 + m.id, 20, m.id, now(), false, 0
FROM sys_menu m
WHERE m.id IN (807)
  AND NOT EXISTS (SELECT 1 FROM sys_role_menu rm WHERE rm.role_id = 20 AND rm.menu_id = m.id);
