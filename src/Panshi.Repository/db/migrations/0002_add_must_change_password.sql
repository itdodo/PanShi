-- 0002：sys_user 增加 must_change_password（首登/重置后强制改密）
-- ⚠️ 幂等：IF NOT EXISTS；历史迁移禁改。CodeFirst 也会按实体 DefaultValue 建列，此脚本为版本化记录 + 兜底。
ALTER TABLE sys_user ADD COLUMN IF NOT EXISTS must_change_password boolean NOT NULL DEFAULT false;
