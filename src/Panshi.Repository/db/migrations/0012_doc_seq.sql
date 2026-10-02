-- 0012：单据号发号器表 sys_doc_seq 的唯一索引 + 历史号段回填
-- 表本身由 CodeFirst 建（实体 SysDocSeq）；这里补上 ON CONFLICT 依赖的唯一索引，
-- 并把「建表前就已存在」的单号段回填进去，避免新号与历史号撞车。
-- ⚠️ 幂等：IF NOT EXISTS + ON CONFLICT DO NOTHING；历史迁移禁改。

CREATE UNIQUE INDEX IF NOT EXISTS uk_sys_doc_seq_key ON sys_doc_seq (seq_key) WHERE is_deleted = false;

INSERT INTO sys_doc_seq (id, seq_key, seq_value, create_time, is_deleted, version)
SELECT floor(extract(epoch from now()) * 1000000)::bigint + row_number() over (order by t.k),
       t.k, t.n, now(), false, 0
FROM (
    SELECT regexp_replace(doc_no, '[0-9]{3}$', '') AS k, max(right(doc_no, 3)::int) AS n
    FROM biz_expense WHERE doc_no ~ '^[A-Z]+[0-9]{8}[0-9]{3}$' GROUP BY 1
    UNION ALL
    SELECT regexp_replace(doc_no, '[0-9]{3}$', ''), max(right(doc_no, 3)::int)
    FROM biz_purchase_request WHERE doc_no ~ '^[A-Z]+[0-9]{8}[0-9]{3}$' GROUP BY 1
    UNION ALL
    SELECT regexp_replace(doc_no, '[0-9]{3}$', ''), max(right(doc_no, 3)::int)
    FROM scm_purchase_order WHERE doc_no ~ '^[A-Z]+[0-9]{8}[0-9]{3}$' GROUP BY 1
    UNION ALL
    SELECT regexp_replace(doc_no, '[0-9]{3}$', ''), max(right(doc_no, 3)::int)
    FROM scm_sales_order WHERE doc_no ~ '^[A-Z]+[0-9]{8}[0-9]{3}$' GROUP BY 1
    UNION ALL
    SELECT regexp_replace(doc_no, '[0-9]{3}$', ''), max(right(doc_no, 3)::int)
    FROM scm_stock_doc WHERE doc_no ~ '^[A-Z]+[0-9]{8}[0-9]{3}$' GROUP BY 1
) t
-- ⚠️ 冲突目标是「部分唯一索引」，ON CONFLICT 必须带上同款索引谓词 where is_deleted = false，
-- 否则 PG 报 42P10: there is no unique or exclusion constraint matching the ON CONFLICT specification
ON CONFLICT (seq_key) WHERE is_deleted = false DO NOTHING;
