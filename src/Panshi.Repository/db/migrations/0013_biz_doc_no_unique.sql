-- 0013：把五张单据表的「存活行单号唯一」兜底补齐
--
-- 为什么两张 biz 表没有：发号在 0012 才改成 sys_doc_seq 原子取号，此前是 COUNT(前缀)+1，
-- 已删单子的号会被重新发出去——压测那天就在 biz_expense 里看到两行 BX20260928001（都已软删）。
--
-- 为什么 scm_purchase_order / scm_sales_order 也要再来一遍：0005 里本来就有这两条 CREATE UNIQUE INDEX，
-- 但它们是**事后追加进一个已存在的迁移**的，而执行器按版本号记流水（sys_db_migration），版本 5 应用过的库
-- 不会重跑这个文件——于是老库里这两张表至今没有兜底，新库里却有。⚠️ 教训：迁移文件一旦在某些库上应用过，
-- 改它对这些库等于没改，要生效必须新起一个编号。这里用 IF NOT EXISTS 向前补，两类库都会收敛到同一个状态。
--
-- 口径与既有索引严格一致：只约束 is_deleted = false 的存活行，历史软删行按「旧数据不动」保留，
-- 所以不必清任何数据（建之前先核对存活行确实无重号）。
-- ⚠️ 幂等：IF NOT EXISTS；历史迁移禁改。

CREATE UNIQUE INDEX IF NOT EXISTS uk_biz_expense_doc_no ON biz_expense (doc_no) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_biz_purchase_request_doc_no ON biz_purchase_request (doc_no) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_scm_purchase_order_doc_no ON scm_purchase_order (doc_no) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_scm_sales_order_doc_no ON scm_sales_order (doc_no) WHERE is_deleted = false;
