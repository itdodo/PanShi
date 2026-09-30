-- 0004：基础资料（物料/供应商/客户）——菜单、admin 授权、编码唯一索引、配套字典
-- ⚠️ 幂等：ON CONFLICT DO NOTHING + NOT EXISTS 双保险；历史迁移禁改。
-- 引导顺序是 CodeFirst → 迁移 → 种子（见 Program.cs 启动块），三张 md_* 表由 CodeFirst 建，
-- 所以这里的 CREATE INDEX 一定跑在表之后；本文件插的固定菜单 ID 由 DbSeeder 的
-- 「按已存在 id 过滤」兜住重复（0003 当年没兜住，全新库直接崩在 sys_menu_pkey）。

-- ===== 一级菜单排序：基础资料排在业务模块之前（它是单据的输入），后三档顺移 =====
-- 全新库上这三条 UPDATE 命中 0 行（目录还没插），由 DbSeeder 里的新 sort 保证顺序。
UPDATE sys_menu SET sort = 5 WHERE id = 4 AND sort <> 5;
UPDATE sys_menu SET sort = 6 WHERE id = 5 AND sort <> 6;
UPDATE sys_menu SET sort = 7 WHERE id = 6 AND sort <> 7;

-- ===== 菜单与权限码 =====
INSERT INTO sys_menu (id, parent_id, menu_name, menu_type, path, component, permission, icon, visible, status,
                      sort, create_time, is_deleted, version)
VALUES
    (7,    null, '基础资料', 1, null,               null,                      null,                      'lucide:package', true,  0, 4, now(), false, 0),
    (701,  7,    '物料',     2, '/basedata/material', 'basedata/material/index', 'basedata:material:list',  'lucide:boxes',   true,  0, 1, now(), false, 0),
    (7011, 701,  '新增',     3, null,               null,                      'basedata:material:add',   null,             false, 0, 0, now(), false, 0),
    (7012, 701,  '编辑',     3, null,               null,                      'basedata:material:edit',  null,             false, 0, 0, now(), false, 0),
    (7013, 701,  '删除',     3, null,               null,                      'basedata:material:delete',null,             false, 0, 0, now(), false, 0),
    (702,  7,    '供应商',   2, '/basedata/supplier', 'basedata/supplier/index', 'basedata:supplier:list',  'lucide:truck',   true,  0, 2, now(), false, 0),
    (7021, 702,  '新增',     3, null,               null,                      'basedata:supplier:add',   null,             false, 0, 0, now(), false, 0),
    (7022, 702,  '编辑',     3, null,               null,                      'basedata:supplier:edit',  null,             false, 0, 0, now(), false, 0),
    (7023, 702,  '删除',     3, null,               null,                      'basedata:supplier:delete',null,             false, 0, 0, now(), false, 0),
    (703,  7,    '客户',     2, '/basedata/customer', 'basedata/customer/index', 'basedata:customer:list',  'lucide:users',   true,  0, 3, now(), false, 0),
    (7031, 703,  '新增',     3, null,               null,                      'basedata:customer:add',   null,             false, 0, 0, now(), false, 0),
    (7032, 703,  '编辑',     3, null,               null,                      'basedata:customer:edit',  null,             false, 0, 0, now(), false, 0),
    (7033, 703,  '删除',     3, null,               null,                      'basedata:customer:delete',null,             false, 0, 0, now(), false, 0)
ON CONFLICT (id) DO NOTHING;

-- admin 角色补授权（超管本就绕过权限校验，但授权弹窗与角色页要能看到这些项）
-- id 用 700000000000000 + menu_id：雪花 id 单调递增且当前已到 8.5e14，这个段是历史空洞，撞不上。
INSERT INTO sys_role_menu (id, role_id, menu_id, create_time, is_deleted, version)
SELECT 700000000000000 + m.id, 20, m.id, now(), false, 0
FROM sys_menu m
WHERE m.id IN (7, 701, 7011, 7012, 7013, 702, 7021, 7022, 7023, 703, 7031, 7032, 7033)
  AND NOT EXISTS (SELECT 1 FROM sys_role_menu rm WHERE rm.role_id = 20 AND rm.menu_id = m.id);

-- ===== 编码唯一（软删过滤）：与 sys_position/sys_dict_type 同一口径 =====
CREATE UNIQUE INDEX IF NOT EXISTS uk_md_material_material_code ON md_material (material_code) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_md_supplier_supplier_code ON md_supplier (supplier_code) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_md_customer_customer_code ON md_customer (customer_code) WHERE is_deleted = false;

-- ===== 配套字典（分类/单位/等级走字典管理，不新建表） =====
-- id 段刻意用 7100+/71001+，避开 DbSeeder 的 typeId(100 起)/dataId(1000 起) 计数区，
-- 否则将来种子多几个字典就会撞到这里。
INSERT INTO sys_dict_type (id, dict_name, dict_code, remark, create_time, is_deleted, version)
VALUES (7100, '物料分类', 'md_material_category', '基础资料-物料的分类', now(), false, 0),
       (7101, '计量单位', 'md_unit',              '基础资料-物料基本单位', now(), false, 0),
       (7102, '供应商分类', 'md_supplier_category', '基础资料-供应商分类', now(), false, 0),
       (7103, '客户等级', 'md_customer_level',    '基础资料-客户等级', now(), false, 0)
ON CONFLICT (id) DO NOTHING;

INSERT INTO sys_dict_data (id, dict_type_id, label, value, sort, status, tag_type, is_default, create_time, is_deleted, version)
VALUES (71001, 7100, '原材料',   'raw',       1, 0, 'info',    false, now(), false, 0),
       (71002, 7100, '半成品',   'semi',      2, 0, 'warning', false, now(), false, 0),
       (71003, 7100, '成品',     'finished',  3, 0, 'success', false, now(), false, 0),
       (71004, 7100, '辅料',     'auxiliary', 4, 0, 'default', false, now(), false, 0),
       (71005, 7100, '包装物',   'packing',   5, 0, 'default', false, now(), false, 0),
       (71011, 7101, '个',       'pcs',       1, 0, 'default', false, now(), false, 0),
       (71012, 7101, '件',       'item',      2, 0, 'default', false, now(), false, 0),
       (71013, 7101, '套',       'set',       3, 0, 'default', false, now(), false, 0),
       (71014, 7101, '千克',     'kg',        4, 0, 'default', false, now(), false, 0),
       (71015, 7101, '吨',       't',         5, 0, 'default', false, now(), false, 0),
       (71016, 7101, '米',       'm',         6, 0, 'default', false, now(), false, 0),
       (71017, 7101, '升',       'l',         7, 0, 'default', false, now(), false, 0),
       (71021, 7102, '原材料商', 'material',  1, 0, 'info',    false, now(), false, 0),
       (71022, 7102, '设备商',   'equipment', 2, 0, 'warning', false, now(), false, 0),
       (71023, 7102, '服务商',   'service',   3, 0, 'success', false, now(), false, 0),
       (71024, 7102, '物流商',   'logistics', 4, 0, 'default', false, now(), false, 0),
       (71031, 7103, 'A 类',     'A',         1, 0, 'danger',  false, now(), false, 0),
       (71032, 7103, 'B 类',     'B',         2, 0, 'warning', false, now(), false, 0),
       (71033, 7103, 'C 类',     'C',         3, 0, 'info',    false, now(), false, 0)
ON CONFLICT (id) DO NOTHING;
