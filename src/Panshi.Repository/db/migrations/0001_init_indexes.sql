-- 0001：唯一过滤索引（软删除过滤）+ 外键/查询索引
-- ⚠️ 幂等：全部 IF NOT EXISTS；历史迁移禁改。

-- ===== 唯一（WHERE is_deleted = false）=====
CREATE UNIQUE INDEX IF NOT EXISTS uk_sys_user_user_name ON sys_user (user_name) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_sys_role_role_code ON sys_role (role_code) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_sys_dept_dept_code ON sys_dept (dept_code) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_sys_position_position_code ON sys_position (position_code) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_sys_dict_type_dict_code ON sys_dict_type (dict_code) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_sys_config_config_key ON sys_config (config_key) WHERE is_deleted = false;
CREATE UNIQUE INDEX IF NOT EXISTS uk_sys_flow_binding_business_table ON sys_flow_binding (business_table) WHERE is_deleted = false;

-- ===== 关联表双外键索引 =====
CREATE INDEX IF NOT EXISTS idx_sys_user_role_user_id ON sys_user_role (user_id);
CREATE INDEX IF NOT EXISTS idx_sys_user_role_role_id ON sys_user_role (role_id);
CREATE INDEX IF NOT EXISTS idx_sys_role_menu_role_id ON sys_role_menu (role_id);
CREATE INDEX IF NOT EXISTS idx_sys_role_menu_menu_id ON sys_role_menu (menu_id);
CREATE INDEX IF NOT EXISTS idx_sys_role_dept_role_id ON sys_role_dept (role_id);
CREATE INDEX IF NOT EXISTS idx_sys_role_dept_dept_id ON sys_role_dept (dept_id);
CREATE INDEX IF NOT EXISTS idx_sys_user_position_user_id ON sys_user_position (user_id);
CREATE INDEX IF NOT EXISTS idx_sys_user_position_position_id ON sys_user_position (position_id);

-- ===== 会话 =====
CREATE INDEX IF NOT EXISTS idx_sys_user_session_token_id ON sys_user_session (token_id);
CREATE INDEX IF NOT EXISTS idx_sys_user_session_refresh_hash ON sys_user_session (refresh_token_hash);
CREATE INDEX IF NOT EXISTS idx_sys_user_session_user_id ON sys_user_session (user_id);

-- ===== 组织/菜单 =====
CREATE INDEX IF NOT EXISTS idx_sys_user_dept_id ON sys_user (dept_id);
CREATE INDEX IF NOT EXISTS idx_sys_menu_parent_id ON sys_menu (parent_id);
CREATE INDEX IF NOT EXISTS idx_sys_dept_parent_id ON sys_dept (parent_id);

-- ===== 字典/参数 =====
CREATE INDEX IF NOT EXISTS idx_sys_dict_data_dict_type_id ON sys_dict_data (dict_type_id);

-- ===== 消息/公告 =====
CREATE INDEX IF NOT EXISTS idx_sys_message_receiver_read ON sys_message (receiver_id, is_read);
CREATE INDEX IF NOT EXISTS idx_sys_notice_status_publish ON sys_notice (status, publish_time);

-- ===== 日志 =====
CREATE INDEX IF NOT EXISTS idx_sys_operation_log_create_time ON sys_operation_log (create_time);
CREATE INDEX IF NOT EXISTS idx_sys_operation_log_user_id ON sys_operation_log (user_id);
CREATE INDEX IF NOT EXISTS idx_sys_login_log_create_time ON sys_login_log (create_time);
CREATE INDEX IF NOT EXISTS idx_sys_change_log_table_record ON sys_change_log (table_name, record_id);

-- ===== 审批流 =====
CREATE INDEX IF NOT EXISTS idx_sys_flow_definition_code ON sys_flow_definition (flow_code);
CREATE INDEX IF NOT EXISTS idx_sys_flow_instance_business ON sys_flow_instance (business_table, business_id);
CREATE INDEX IF NOT EXISTS idx_sys_flow_instance_submitter ON sys_flow_instance (submitter_id);
CREATE INDEX IF NOT EXISTS idx_sys_flow_instance_status ON sys_flow_instance (status);
CREATE INDEX IF NOT EXISTS idx_sys_flow_task_instance ON sys_flow_task (instance_id);
CREATE INDEX IF NOT EXISTS idx_sys_flow_task_approver_status ON sys_flow_task (approver_user_id, status);
CREATE INDEX IF NOT EXISTS idx_sys_flow_record_instance ON sys_flow_record (instance_id);
CREATE INDEX IF NOT EXISTS idx_sys_flow_cc_user_read ON sys_flow_cc (user_id, is_read);

-- ===== 业务样板 =====
CREATE INDEX IF NOT EXISTS idx_biz_expense_owner ON biz_expense (owner_user_id);
CREATE INDEX IF NOT EXISTS idx_biz_expense_dept ON biz_expense (dept_id);
CREATE INDEX IF NOT EXISTS idx_biz_purchase_request_owner ON biz_purchase_request (owner_user_id);
CREATE INDEX IF NOT EXISTS idx_biz_purchase_request_dept ON biz_purchase_request (dept_id);
