-- 0007：供应链 P1——出入库单 / 库存台账 / 库存流水 的菜单、权限码与索引
-- ⚠️ 幂等：WHERE NOT EXISTS + ON CONFLICT (id) DO NOTHING；历史迁移禁改。
-- 新菜单只写这里、不写进 DbSeeder（0003 的教训：两边都写同一批固定 ID，全新库会撞 sys_menu_pkey）。

INSERT INTO sys_menu (id, parent_id, menu_name, menu_type, path, component, permission, icon, visible, status,
                      sort, create_time, is_deleted, version)
VALUES
    (803,  8,    '出入库单', 2, '/scm/stock-doc', 'scm/stock-doc/index', 'scm:stockdoc:list',
            'lucide:arrow-left-right', true, 0, 3, now(), false, 0),
    (8031, 803,  '新增',     3, null, null, 'scm:stockdoc:add',    null, false, 0, 0, now(), false, 0),
    (8032, 803,  '编辑',     3, null, null, 'scm:stockdoc:edit',   null, false, 0, 0, now(), false, 0),
    (8033, 803,  '删除',     3, null, null, 'scm:stockdoc:delete', null, false, 0, 0, now(), false, 0),
    -- 过账与作废单独一个码：能录单的人不该都能改库存
    (8034, 803,  '过账/作废', 3, null, null, 'scm:stockdoc:post',  null, false, 0, 0, now(), false, 0),
    (804,  8,    '库存台账', 2, '/scm/stock', 'scm/stock/index', 'scm:stock:list',
            'lucide:layers', true, 0, 4, now(), false, 0),
    (805,  8,    '库存流水', 2, '/scm/ledger', 'scm/ledger/index', 'scm:ledger:list',
            'lucide:history', true, 0, 5, now(), false, 0)
ON CONFLICT (id) DO NOTHING;

INSERT INTO sys_role_menu (id, role_id, menu_id, create_time, is_deleted, version)
SELECT 700000000000000 + m.id, 20, m.id, now(), false, 0
FROM sys_menu m
WHERE m.id IN (803, 8031, 8032, 8033, 8034, 804, 805)
  AND NOT EXISTS (SELECT 1 FROM sys_role_menu rm WHERE rm.role_id = 20 AND rm.menu_id = m.id);

-- ===== 唯一约束 =====
CREATE UNIQUE INDEX IF NOT EXISTS uk_scm_stock_doc_doc_no ON scm_stock_doc (doc_no) WHERE is_deleted = false;
-- 台账一行一个「仓库 × 物料」：过账靠它定位要更新哪一行，没有唯一索引就会长出重复行
CREATE UNIQUE INDEX IF NOT EXISTS uk_scm_stock_warehouse_material
    ON scm_stock (warehouse_id, material_id) WHERE is_deleted = false;

-- ===== 查询索引 =====
CREATE INDEX IF NOT EXISTS idx_scm_stock_doc_line_doc_id ON scm_stock_doc_line (doc_id);
CREATE INDEX IF NOT EXISTS idx_scm_stock_doc_warehouse_id ON scm_stock_doc (warehouse_id);
CREATE INDEX IF NOT EXISTS idx_scm_stock_ledger_doc_id ON scm_stock_ledger (doc_id);
CREATE INDEX IF NOT EXISTS idx_scm_stock_ledger_material_time ON scm_stock_ledger (material_id, biz_time);
CREATE INDEX IF NOT EXISTS idx_scm_stock_warehouse_id ON scm_stock (warehouse_id);
