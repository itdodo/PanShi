-- 0003：安全 P1「IP 黑白名单」菜单与权限码
-- ⚠️ 幂等：ON CONFLICT DO NOTHING + NOT EXISTS 双保险；历史迁移禁改。
-- 为什么走迁移而不是改 DbSeeder：SeedMenus 有 __seed_v1__ 哨兵，老库直接 return，
-- 新菜单只加进种子脚本的话，已部署的库永远看不到它。
INSERT INTO sys_menu (id, parent_id, menu_name, menu_type, path, component, permission, icon, visible, status,
                      sort, create_time, is_deleted, version)
VALUES (604, 6, 'IP 黑白名单', 2, '/monitor/ipguard', 'monitor/ipguard/index', 'monitor:ipguard:list',
        'lucide:shield', true, 0, 4, now(), false, 0),
       (6041, 604, '管理', 3, null, null, 'monitor:ipguard:manage', null, true, 0, 1, now(), false, 0)
ON CONFLICT (id) DO NOTHING;

-- admin 角色补授权（超管本就绕过权限校验，但授权弹窗与角色页要能看到这两项）
INSERT INTO sys_role_menu (id, role_id, menu_id, create_time, is_deleted, version)
SELECT v.id, 20, v.menu_id, now(), false, 0
FROM (VALUES (604000000000604, 604), (604000000000641, 6041)) AS v(id, menu_id)
WHERE NOT EXISTS (SELECT 1 FROM sys_role_menu rm WHERE rm.role_id = 20 AND rm.menu_id = v.menu_id);
