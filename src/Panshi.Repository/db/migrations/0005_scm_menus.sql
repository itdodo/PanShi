-- 0005：供应链管理 P0——采购/销售订单菜单与索引，基础资料补仓库与协议价菜单
-- ⚠️ 幂等：ON CONFLICT DO NOTHING + NOT EXISTS；历史迁移禁改。
-- 新菜单只写在这里、不写进 DbSeeder（沿用 0004 定的规矩）：全新库上迁移先跑、种子后插，
-- 两边都写同一批固定 ID 就会撞 sys_menu_pkey（0003 当年正是这么崩的）。

-- ===== 一级菜单排序：供应链管理插在基础资料之后，后三档顺移 =====
UPDATE sys_menu SET sort = 6 WHERE id = 4 AND sort <> 6;
UPDATE sys_menu SET sort = 7 WHERE id = 5 AND sort <> 7;
UPDATE sys_menu SET sort = 8 WHERE id = 6 AND sort <> 8;

INSERT INTO sys_menu (id, parent_id, menu_name, menu_type, path, component, permission, icon, visible, status,
                      sort, create_time, is_deleted, version)
VALUES
    -- 供应链管理
    (8,    null, '供应链管理', 1, null, null, null, 'lucide:truck', true, 0, 5, now(), false, 0),
    (801,  8,    '采购订单',   2, '/scm/purchase-order', 'scm/purchase-order/index', 'scm:purchase:list',
            'lucide:shopping-cart', true, 0, 1, now(), false, 0),
    (8011, 801,  '新增',       3, null, null, 'scm:purchase:add',    null, false, 0, 0, now(), false, 0),
    (8012, 801,  '编辑',       3, null, null, 'scm:purchase:edit',   null, false, 0, 0, now(), false, 0),
    (8013, 801,  '删除',       3, null, null, 'scm:purchase:delete', null, false, 0, 0, now(), false, 0),
    (8014, 801,  '提交审批',   3, null, null, 'scm:purchase:submit', null, false, 0, 0, now(), false, 0),
    (802,  8,    '销售订单',   2, '/scm/sales-order', 'scm/sales-order/index', 'scm:sales:list',
            'lucide:tag', true, 0, 2, now(), false, 0),
    (8021, 802,  '新增',       3, null, null, 'scm:sales:add',    null, false, 0, 0, now(), false, 0),
    (8022, 802,  '编辑',       3, null, null, 'scm:sales:edit',   null, false, 0, 0, now(), false, 0),
    (8023, 802,  '删除',       3, null, null, 'scm:sales:delete', null, false, 0, 0, now(), false, 0),
    (8024, 802,  '提交审批',   3, null, null, 'scm:sales:submit', null, false, 0, 0, now(), false, 0),
    -- 基础资料补两张：仓库、采购协议价
    (704,  7,    '仓库',       2, '/basedata/warehouse', 'basedata/warehouse/index', 'basedata:warehouse:list',
            'lucide:warehouse', true, 0, 4, now(), false, 0),
    (7041, 704,  '新增',       3, null, null, 'basedata:warehouse:add',    null, false, 0, 0, now(), false, 0),
    (7042, 704,  '编辑',       3, null, null, 'basedata:warehouse:edit',   null, false, 0, 0, now(), false, 0),
    (7043, 704,  '删除',       3, null, null, 'basedata:warehouse:delete', null, false, 0, 0, now(), false, 0),
    (705,  7,    '采购价目表', 2, '/basedata/price', 'basedata/price/index', 'basedata:price:list',
            'lucide:badge-dollar', true, 0, 5, now(), false, 0),
    (7051, 705,  '新增',       3, null, null, 'basedata:price:add',    null, false, 0, 0, now(), false, 0),
    (7052, 705,  '编辑',       3, null, null, 'basedata:price:edit',   null, false, 0, 0, now(), false, 0),
    (7053, 705,  '删除',       3, null, null, 'basedata:price:delete', null, false, 0, 0, now(), false, 0)
ON CONFLICT (id) DO NOTHING;

-- admin 角色补授权（超管本就绕过权限校验，但授权弹窗与角色页要能看到这些项）
INSERT INTO sys_role_menu (id, role_id, menu_id, create_time, is_deleted, version)
SELECT 700000000000000 + m.id, 20, m.id, now(), false, 0
FROM sys_menu m
WHERE m.id IN (8, 801, 8011, 8012, 8013, 8014, 802, 8021, 8022, 8023, 8024,
               704, 7041, 7042, 7043, 705, 7051, 7052, 7053)
  AND NOT EXISTS (SELECT 1 FROM sys_role_menu rm WHERE rm.role_id = 20 AND rm.menu_id = m.id);

-- ===== 唯一约束（软删过滤）：单号与编码不允许重复 =====
CREATE UNIQUE INDEX IF NOT EXISTS uk_scm_purchase_order_doc_no ON scm_purchase_order (doc_no) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_scm_sales_order_doc_no ON scm_sales_order (doc_no) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_md_warehouse_warehouse_code ON md_warehouse (warehouse_code) WHERE is_deleted = false;
-- 一个供应商 × 一个物料只留一行现行协议价（调价改这一行，历史走字段级变更日志）
CREATE UNIQUE INDEX IF NOT EXISTS uk_md_price_agreement_supplier_material
    ON md_price_agreement (supplier_id, material_id) WHERE is_deleted = false;

-- ===== 明细按头查、列表按往来单位筛 =====
CREATE INDEX IF NOT EXISTS idx_scm_purchase_order_line_order_id ON scm_purchase_order_line (order_id);
CREATE INDEX IF NOT EXISTS idx_scm_sales_order_line_order_id ON scm_sales_order_line (order_id);
CREATE INDEX IF NOT EXISTS idx_scm_purchase_order_supplier_id ON scm_purchase_order (supplier_id);
CREATE INDEX IF NOT EXISTS idx_scm_sales_order_customer_id ON scm_sales_order (customer_id);
CREATE INDEX IF NOT EXISTS idx_md_price_agreement_supplier_id ON md_price_agreement (supplier_id);
