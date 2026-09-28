# Panshi — 企业级单机管理中后台底座

.NET 10 + PostgreSQL 17 + Vue 3.5 + **Naive UI** 的完整后台框架：认证与会话治理、RBAC（角色管菜单权限 / 岗位管审批权限）、部门数据权限五档、钉钉式审批流引擎（可视化 DSL、会签/或签/依次/条件分支/驳回/加签/抄送）、审计三件套（操作/登录/字段级变更）、站内信 + SignalR 实时、定时任务 + 每日自动备份、报销单/采购申请单业务样板。

- 重建规范：`docs/框架蓝图.md`
- 现状文档：`docs/技术文档.md`（架构、28 表、API 一览、18 条红线对照、测试基线）
- 前端约定：`docs/前端页面规约.md`

## 快速开始

```bash
# 1) 起数据库（或全套含后端）
docker compose up -d panshi-db

# 2) 后端（开发态，http://localhost:5125，/swagger）
dotnet run --project src/Panshi.Api

# 3) 前端（http://localhost:5173，代理 /api 与 /hubs）
cd web && npm install && npm run dev

# 一体化生产形态
docker compose up -d --build     # http://localhost:18080（API+SPA 单容器；主机 18080 → 容器 8080）
```

## 验收（提交前必跑）

```bash
bash scripts/verify.sh          # 后端编译 + 单元 + 集成（需 panshi-db 在跑）+ 前端类型检查 + 前端构建
bash scripts/verify.sh --fast   # 跳过集成测试那一步
```

任何一步红就退出码非 0，并指名是哪一步。**别带着红的步骤去 build 镜像。**
`--fast` 之外还会在 `panshi-db` 没起时直接报错提示，而不是把集成测试静默跑成一片红。

> 为什么单独强调「前端类型检查」：`vite build` 不做类型检查，所以类型错误可以让构建一路绿着过去。
> 这一轮就撞上过——`@wangeditor/editor-for-vue` 的 `exports` 没暴露类型入口，`vue-tsc` 红了很久，
> 但没人被拦下来。现在它是验收的第 4 步。

首次启动自动完成：建库建表（CodeFirst）→ 版本化迁移 → 内置种子（管理员/角色/菜单/字典/参数）。
管理员初始口令见 `src/Panshi.Repository/DbSeeder.cs`，**登录后请立即修改**。生产部署必须用环境变量覆盖 `Jwt__SecretKey` 与数据库密码。

## 质量命令

```bash
dotnet build Panshi.slnx            # 0 错误 0 警告
dotnet test Panshi.slnx             # 单元 61 + 集成 29（需 docker 起 panshi-db，测试库 panshi_test）
cd web && npx vue-tsc --noEmit && npm run build
```

## 技术选型（与旧蓝图的差异）

| 项 | 本实现 | 原蓝图 | 原因 |
|---|---|---|---|
| UI 组件库 | **Naive UI** + iconify lucide 图标 | Element Plus | 更高级的视觉与 TS 原生主题系统 |
| 数据库 | **PostgreSQL 17**（Docker） | SqlServer | 免许可、容器友好；备份=pg_dump；唯一约束用过滤索引 |
| 执行 | 按蓝图从零重建 | — | 用户决策 |
