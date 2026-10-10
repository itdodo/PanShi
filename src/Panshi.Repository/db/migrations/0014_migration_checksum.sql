-- 0014：迁移流水表加内容指纹列 checksum
--
-- 为什么加：sys_db_migration 只按版本号记账，「改动一个已经应用过的迁移」在过去完全无声。
-- 实测栽过一次：0005 里 scm 两张单据表的索引语句是在**提交之前**被测试库应用掉的（那一版还没有这两行），
-- 后来把它们追加进同一个文件，版本 5 早已记账 → 永不重跑 → 老库缺索引、新库有索引。
-- 现在执行器每次启动都比对指纹，不符就拒绝启动（见 DbMigrationRunner.Guard）。
--
-- ⚠️ 列必须显式写 varchar(64)：给已存在的表加 string 列时，SqlSugar 的 CodeFirst ALTER 路径会生成
-- text(64)，PG 报 42601。实体上同样写了 ColumnDataType，两边口径一致。
-- 历史行留 NULL，由执行器第一次遇到时按当前内容补记（一次性豁免，之后不符即拦）。
-- ⚠️ 幂等：IF NOT EXISTS；历史迁移禁改。

ALTER TABLE sys_db_migration ADD COLUMN IF NOT EXISTS checksum varchar(64);
