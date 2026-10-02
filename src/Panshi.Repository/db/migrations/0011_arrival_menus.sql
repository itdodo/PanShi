-- 0011：到货计划的菜单、权限码与索引
-- ⚠️ 幂等：WHERE NOT EXISTS + ON CONFLICT (id) DO NOTHING；历史迁移禁改。
-- 新菜单只写这里、不写进 DbSeeder（0003 的教训：两边都写同一批固定 ID，全新库会撞 sys_menu_pkey）。

INSERT INTO sys_menu (id, parent_id, menu_name, menu_type, path, component, permission, icon, visible, status,
                      sort, create_time, is_deleted, version)
VALUES
    (809,  8,   '到货计划', 2, '/scm/arrival', 'scm/arrival/index', 'scm:arrival:list',
            'lucide:truck', true, 0, 9, now(), false, 0),
    -- 计划本身不给增删：它由采购订单批准后按行生成，人工只能改期
    (8091, 809, '改期',     3, null, null, 'scm:arrival:edit', null, false, 0, 0, now(), false, 0)
ON CONFLICT (id) DO NOTHING;

INSERT INTO sys_role_menu (id, role_id, menu_id, create_time, is_deleted, version)
SELECT 700000000000000 + m.id, 20, m.id, now(), false, 0
FROM sys_menu m
WHERE m.id IN (809, 8091)
  AND NOT EXISTS (SELECT 1 FROM sys_role_menu rm WHERE rm.role_id = 20 AND rm.menu_id = m.id);

-- ===== 唯一约束 =====
-- 一条订单行只能有一条计划：生成侧本来就只补缺失行，这条索引兜住并发重复生成
CREATE UNIQUE INDEX IF NOT EXISTS uk_scm_purchase_arrival_line
    ON scm_purchase_arrival (order_line_id) WHERE is_deleted = false;

-- ===== 查询索引 =====
CREATE INDEX IF NOT EXISTS idx_scm_purchase_arrival_order_id ON scm_purchase_arrival (order_id);
CREATE INDEX IF NOT EXISTS idx_scm_purchase_arrival_plan_date ON scm_purchase_arrival (plan_date);
CREATE INDEX IF NOT EXISTS idx_scm_purchase_arrival_supplier_id ON scm_purchase_arrival (supplier_id);
