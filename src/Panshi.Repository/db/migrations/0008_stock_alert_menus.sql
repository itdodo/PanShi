-- 0008：库存预警——菜单与权限码，并给演示物料设一组阈值（阈值列由 CodeFirst 加在 md_material 上）
-- ⚠️ 幂等：WHERE NOT EXISTS + ON CONFLICT (id) DO NOTHING；历史迁移禁改。
-- 新菜单只写这里、不写进 DbSeeder（0003 的教训：两边都写同一批固定 ID，全新库会撞 sys_menu_pkey）。

INSERT INTO sys_menu (id, parent_id, menu_name, menu_type, path, component, permission, icon, visible, status,
                      sort, create_time, is_deleted, version)
VALUES
    (806, 8, '库存预警', 2, '/scm/stock-alert', 'scm/stock-alert/index', 'scm:alert:list',
            'lucide:triangle-alert', true, 0, 6, now(), false, 0)
ON CONFLICT (id) DO NOTHING;

INSERT INTO sys_role_menu (id, role_id, menu_id, create_time, is_deleted, version)
SELECT 700000000000000 + m.id, 20, m.id, now(), false, 0
FROM sys_menu m
WHERE m.id IN (806)
  AND NOT EXISTS (SELECT 1 FROM sys_role_menu rm WHERE rm.role_id = 20 AND rm.menu_id = m.id);

-- ===== 演示阈值 =====
-- 只补没设过的行（min_stock IS NULL），不覆盖用户自己调过的参数。
-- 下限=安全库存、上限=最高储备；两者都空表示该物料不参与预警。
UPDATE md_material AS m
SET min_stock = v.min_stock, max_stock = v.max_stock, version = m.version + 1
FROM (VALUES
    ('RM-ST01', 20::numeric(18,4),   400::numeric(18,4)),
    ('RM-ST02', 500::numeric(18,4),  8000::numeric(18,4)),
    ('RM-CU01', 100::numeric(18,4),  1000::numeric(18,4)),
    ('RM-AL01', 200::numeric(18,4),  2000::numeric(18,4)),
    ('RM-PL01', 300::numeric(18,4),  2000::numeric(18,4)),
    ('FG-01',   5::numeric(18,4),    60::numeric(18,4)),
    ('FG-02',   10::numeric(18,4),   120::numeric(18,4))
) AS v(code, min_stock, max_stock)
WHERE m.material_code = v.code AND m.is_deleted = false AND m.min_stock IS NULL AND m.max_stock IS NULL;
