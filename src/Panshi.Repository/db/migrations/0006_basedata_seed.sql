-- 0006：基础资料初始化演示数据（物料/供应商/客户/仓库/协议价）
-- ⚠️ 幂等：全部走「按业务编码判存在」的 WHERE NOT EXISTS，而不是 ON CONFLICT (id)——
--    库里若已有同编码的不同 id 行（用户自己建过），ON CONFLICT (id) 会绕过唯一索引直接撞 uk_md_*。
-- id 用 7201~7205 开头的 15 位定长段（7.2e14）：雪花 id 单调递增且当前已到 8.5e14，这段是历史空洞，撞不上。
-- 只在新库和已部署库各跑一次；改这套数据请另起 0007，历史迁移禁改。

-- ===== 仓库（3 个，主材仓为默认仓） =====
INSERT INTO md_warehouse (id, warehouse_code, warehouse_name, address, contact, phone, is_default, status,
                          remark, create_time, is_deleted, version)
SELECT v.id, v.code, v.name, v.addr, v.contact, v.phone, v.is_default, 0, v.remark, now(), false, 0
FROM (VALUES
    (720400000000001, 'WH-ZC'::text, '主材仓'::text, '厂区东侧 1 号库'::text, '张仓管'::text, '0512-6600-1001'::text, true::boolean, '原材料与半成品存放，恒温恒湿'::text),
    (720400000000002, 'WH-CG', '成品仓', '厂区南侧 3 号库', '王仓管', '0512-6600-1002', false, '成品待发货区，含打包工位'),
    (720400000000003, 'WH-LX', '零星料仓', '车间二层东廊', '李仓管', '0512-6600-1003', false, '辅料、包材与备件')
) AS v(id, code, name, addr, contact, phone, is_default, remark)
WHERE NOT EXISTS (SELECT 1 FROM md_warehouse w WHERE w.warehouse_code = v.code AND w.is_deleted = false);

-- ===== 物料（12 条，分类/单位取迁移 0004 种的字典 value） =====
INSERT INTO md_material (id, material_code, material_name, category, spec, unit, purchase_price, sale_price,
                         status, remark, create_time, is_deleted, version)
SELECT v.id, v.code, v.name, v.category, v.spec, v.unit, v.purchase, v.sale, 0, v.remark, now(), false, 0
FROM (VALUES
    (720100000000001, 'RM-ST01'::text, '热轧钢板'::text, 'raw'::text, 'Q235B 1000×2000×10.0'::text, 'item'::text, 4520.00::numeric(18,2), 4980.00::numeric(18,2), '常供，按张计'::text),
    (720100000000002, 'RM-ST02', '冷轧板卷', 'raw', 'SPCC 1.0×1250×C', 'kg', 5.60, 6.80, '按重量结算'),
    (720100000000003, 'RM-CU01', '电解铜带', 'raw', 'T2 0.3×100×C', 'kg', 68.50, 76.00, '价格随行就市，需按周核'),
    (720100000000004, 'RM-AL01', '铝合金型材', 'raw', '6063-T5 40×40×3000', 'm', 23.80, 28.50, '氧化银白'),
    (720100000000005, 'RM-PL01', 'ABS 塑料粒子', 'raw', '750 白色', 'kg', 13.20, 15.60, '25kg/袋'),
    (720100000000006, 'SB-ST01', '冲压支架半成品', 'semi', '按图 JQ-201 3.0mm', 'pcs', null, null, '自制件，无外购价'),
    (720100000000007, 'SB-ST02', '折弯侧板半成品', 'semi', '按图 JQ-202 2.0mm', 'pcs', null, null, '自制件'),
    (720100000000008, 'FG-01', '机柜外壳组件', 'finished', '600×600×1200 RAL7035', 'set', null, 1280.00, '含喷涂与装配'),
    (720100000000009, 'FG-02', '配电箱体', 'finished', '400×300×180 IP54', 'pcs', null, 326.00, '壁挂式'),
    (720100000000010, 'AX-01', '高强度螺栓', 'auxiliary', 'M8×25 8.8 级镀锌', 'pcs', 0.38, null, '1000/盒'),
    (720100000000011, 'AX-02', '环氧粉末涂料', 'auxiliary', 'RAL7035', 'kg', 26.50, null, '喷塑用'),
    (720100000000012, 'PK-01', '五层瓦楞纸箱', 'packing', '650×450×400', 'pcs', 3.20, null, '出口需熏蒸标识')
) AS v(id, code, name, category, spec, unit, purchase, sale, remark)
WHERE NOT EXISTS (SELECT 1 FROM md_material m WHERE m.material_code = v.code AND m.is_deleted = false);

-- ===== 供应商（6 条） =====
INSERT INTO md_supplier (id, supplier_code, supplier_name, short_name, tax_no, contact, phone, email, address,
                         bank_name, bank_account, category, status, remark, create_time, is_deleted, version)
SELECT v.id, v.code, v.name, v.short, v.tax, v.contact, v.phone, v.email, v.addr, v.bank, v.acct, v.category, 0, v.remark, now(), false, 0
FROM (VALUES
    (720200000000001, 'SP-001'::text, '宝钢华东销售中心'::text, '宝钢'::text, '91310000MA1FL1XY2B'::text, '陈明'::text, '021-5888-1201'::text, 'chenming@example.com'::text, '上海市宝山区'::text, '工行上海宝山支行'::text, '1001203409000000001'::text, 'material'::text, '板材主供，月结 60 天'::text),
    (720200000000002, 'SP-002', '宁波铜业股份有限公司', '宁波铜业', '91330200MA2AB3CD4E', '李伟', '0574-8712-2202', 'liwei@example.com', '浙江省宁波市北仑区', '中行宁波北仑支行', '3702015809000000002', 'material', '铜材，款到发货'),
    (720200000000003, 'SP-003', '广达设备制造有限公司', '广达设备', '91440300MA5FG6HI7J', '黄强', '0755-2666-3303', 'huangqiang@example.com', '广东省深圳市宝安区', '招行深圳宝安支行', '7559000000000000003', 'equipment', '冲压与折弯模具'),
    (720200000000004, 'SP-004', '中远物流运输有限公司', '中远物流', '91110108MA01JK2L3M', '周敏', '010-5866-4404', 'zhoumin@example.com', '北京市大兴区', '建行北京大兴支行', '1100500000000000004', 'logistics', '干线整车与零担'),
    (720200000000005, 'SP-005', '恒信表面处理服务有限公司', '恒信表面', '91320200MA1MN4OP5Q', '吴涛', '0510-8233-5505', 'wutao@example.com', '江苏省无锡市新吴区', '江苏银行无锡新吴支行', '3220000000000000005', 'service', '喷塑、电镀外协'),
    (720200000000006, 'SP-006', '三源包装材料厂', '三源包装', '91330100MA2QR6ST7U', '郑霞', '0571-8899-6606', 'zhengxia@example.com', '浙江省杭州市余杭区', '杭州银行余杭支行', '3301000000000000006', 'material', '纸箱与托盘')
) AS v(id, code, name, short, tax, contact, phone, email, addr, bank, acct, category, remark)
WHERE NOT EXISTS (SELECT 1 FROM md_supplier s WHERE s.supplier_code = v.code AND s.is_deleted = false);

-- ===== 客户（6 条） =====
INSERT INTO md_customer (id, customer_code, customer_name, short_name, tax_no, contact, phone, email, address,
                         level, status, remark, create_time, is_deleted, version)
SELECT v.id, v.code, v.name, v.short, v.tax, v.contact, v.phone, v.email, v.addr, v.level, 0, v.remark, now(), false, 0
FROM (VALUES
    (720300000000001, 'CU-001'::text, '华能电力设备有限公司'::text, '华能'::text, '91110108MA01AB12CD'::text, '孙磊'::text, '010-6233-7701'::text, 'sunlei@example.com'::text, '北京市海淀区'::text, 'A'::text, '年框协议，季度对账'::text),
    (720300000000002, 'CU-002', '东方电气集团装备公司', '东方电气', '91510000450723456X', '何军', '0838-2500-7702', 'hejun@example.com', '四川省德阳市', 'A', '招投标为主，账期 90 天'),
    (720300000000003, 'CU-003', '施耐德自动化代理商行', '施耐德代理', '91310115MA1K3WXY2Z', '林芳', '021-3888-7703', 'linfang@example.com', '上海市浦东新区', 'B', '小批量多频次'),
    (720300000000004, 'CU-004', '中车配件采购中心', '中车配件', '91110108MA01CD34EF', '郭涛', '010-5123-7704', 'guotao@example.com', '北京市丰台区', 'A', '需随货质检报告'),
    (720300000000005, 'CU-005', '南方机电贸易有限公司', '南方机电', '91440101MA5AGH123I', '许静', '020-8123-7705', 'xujing@example.com', '广东省广州市天河区', 'B', '现款现货'),
    (720300000000006, 'CU-006', '西部五金批发部', '西部五金', '91610113MA7JKL456M', '邓伟', '029-8877-7706', 'dengwei@example.com', '陕西省西安市雁塔区', 'C', '零散单，月结 30 天')
) AS v(id, code, name, short, tax, contact, phone, email, addr, level, remark)
WHERE NOT EXISTS (SELECT 1 FROM md_customer c WHERE c.customer_code = v.code AND c.is_deleted = false);

-- ===== 协议价（10 条：供应商 × 物料的现行含税价，订单选料时据此带出） =====
-- id 逐行写死而不是 row_number() 生成：row_number 会随「哪些对已被占用」漂移，
-- 重跑时算出的 id 会撞上已存在的行，被 ON CONFLICT 静默吞掉（实测漏插一条）。
INSERT INTO md_price_agreement (id, supplier_id, supplier_name, material_id, material_code, material_name,
                                unit_price, tax_rate, begin_date, end_date, status, remark, create_time, is_deleted, version)
SELECT v.id, s.id, s.supplier_name, m.id, m.material_code, m.material_name,
       v.price, v.tax, v.begin_date, v.end_date, 0, v.remark, now(), false, 0
FROM (VALUES
    (720500000000001, 'SP-001'::text, 'RM-ST01'::text, 4520.00::numeric(18,4), 13::numeric(5,2), '2026-01-01'::date, NULL::date, '年框价，含运费'::text),
    (720500000000002, 'SP-001', 'RM-ST02', 5.60, 13, '2026-01-01', NULL, '按当日网价下浮 3%'),
    (720500000000003, 'SP-002', 'RM-CU01', 68.50, 13, '2026-01-01', '2026-12-31', '周报价，需复核'),
    (720500000000004, 'SP-002', 'RM-AL01', 23.80, 13, '2026-01-01', NULL, '6 米定尺'),
    (720500000000005, 'SP-006', 'RM-PL01', 13.20, 13, '2026-01-01', NULL, '25kg 袋装'),
    (720500000000006, 'SP-003', 'SB-ST01', 12.80, 13, '2026-01-01', NULL, '外协冲压单价，模具费另计'),
    (720500000000007, 'SP-005', 'AX-02', 26.50, 13, '2026-01-01', NULL, '含前处理'),
    (720500000000008, 'SP-005', 'FG-01', 86.00, 13, '2026-01-01', NULL, '喷涂+装配外协单价'),
    (720500000000009, 'SP-006', 'PK-01', 3.20, 13, '2026-01-01', NULL, '五层瓦楞'),
    (720500000000010, 'SP-001', 'AX-01', 0.38, 13, '2026-01-01', NULL, '随主材配套供货')
) AS v(id, sup_code, mat_code, price, tax, begin_date, end_date, remark)
JOIN md_supplier s ON s.supplier_code = v.sup_code AND s.is_deleted = false
JOIN md_material m ON m.material_code = v.mat_code AND m.is_deleted = false
WHERE NOT EXISTS (
    SELECT 1 FROM md_price_agreement p
    WHERE p.supplier_id = s.id AND p.material_id = m.id AND p.is_deleted = false
);
