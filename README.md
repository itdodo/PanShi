# Panshi — 企业级单机管理中后台底座

[![CI](https://github.com/itdodo/PanShi/actions/workflows/ci.yml/badge.svg)](https://github.com/itdodo/PanShi/actions/workflows/ci.yml)

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

### 口令从哪来

仓库里**没有任何真口令**：`appsettings.json` 的 `Db:ConnectionString` 与集成测试的连接串用的都是占位值。

```bash
cp .env.example .env      # 填 PANSHI_DB_PASSWORD 与 PANSHI_JWT_SECRET；.env 已被 .gitignore 忽略
```

| 场景 | 生效的口令 |
| --- | --- |
| `docker compose up` | compose 读 `.env` 做 `${VAR:?}` 替换，并用 `Db__ConnectionString` 覆盖镜像里的占位连接串 |
| `dotnet run`（本机 dev 5125） | 自己导：`export Db__ConnectionString="Host=localhost;Port=5432;Database=panshi;Username=panshi;Password=<.env 里的值>;Pooling=true"` |
| `dotnet test`（集成，库 `panshi_test`） | 优先 `PANSHI_TEST_CONN`；没导就自动从仓库根 `.env` 取 `PANSHI_DB_PASSWORD` 拼，所以 `scripts/verify.sh` 不需要额外配置 |

### 健康检查与接口文档

| 端点 | 含义 |
| --- | --- |
| `GET /api/v1/health` | **存活**探针：进程能应答即 200，不查依赖。容器 `HEALTHCHECK` 打这条——查依赖会让数据库抖一下就把 API 反复判死重启 |
| `GET /api/v1/health/ready` | **就绪**探针：真的 `select 1` 问一次库（3 秒超时），不可用回 503。给反代/编排摘流量与人工排障用 |

`/swagger` 与 `/openapi/v1.json` **仅开发态**暴露（生产等于把全部端点与 DTO 结构白送给任何能访问端口的人）。

### 排障：关联 ID 与指标

每条响应都带 `X-Correlation-Id`，同一次请求在服务端日志里也是同一个号（`[19:41:02 ERR] [c8f3…] …`）。用户报障时把这个号给运维即可定位。上游网关带来的号会被沿用，但只接受 8–64 位的 `[A-Za-z0-9._-]`，脏值一律重新发号。

```bash
curl -si http://127.0.0.1:18080/api/v1/health | grep -i x-correlation-id
# 指标快照（要 monitor:server:list 权限）：状态码分布、平均/最大耗时、GC、线程池、数据库连接数
curl -s -H "Authorization: Bearer $TOKEN" http://127.0.0.1:18080/api/v1/monitor/metrics
```

给的是**自启动累计量**，没有百分位也不引 Prometheus 依赖：要速率就按固定间隔抓两次算差值。

### 强制改密

新建/导入/管理员重置的账号，口令未换之前服务端只放行 4 条端点（改密、看自己资料、刷新令牌、登出），其余一律 401 —— 前端路由的拦截只是提示，真正的门在 JWT 校验里。管理员重置密码会**同时下线该账号全部会话**并站内信通知本人。


## IP 黑白名单（安全 P1）

监控 → IP 黑白名单（`monitor:ipguard:list` / `monitor:ipguard:manage`），接口在 `/api/v1/monitor/ip-rule`。
规则支持裸 IP 与 CIDR，判定顺序是**白 > 黑 > 未命中**，白名单同时豁免限流。

两个开关（`appsettings`，可用环境变量覆盖）：

| 键 | 默认 | 含义 |
|---|---|---|
| `Security:IpGuard:Enabled` | `true` | 关掉即整个闸门直通 |
| `Security:IpGuard:DryRun` | **`true`** | 命中只记 Warning 不拦。**先观察再关它** |

三条必须知道的规则：
1. **防自锁安全栏**：不允许新建覆盖「默认路由 / 受信代理 / 回环段 / 常见 docker 网关段」的黑名单，
   保存即报错。原因见上一节——直连或容器 NAT 下所有客户端显示为同一个来源，封它就是封全站。
2. **健康检查 `/api/v1/health` 永久豁免**，否则一次误封会让编排把容器判死并反复重启。
3. 规则走内存缓存（10 分钟 TTL），**通过界面/接口改会立即生效**；直接改数据库要等 TTL 或重启进程。

## 验收（提交前必跑）

```bash
bash scripts/verify.sh          # 后端编译 + 单元 + 集成（需 panshi-db 在跑）+ 前端类型检查 + 前端构建
bash scripts/verify.sh --fast   # 跳过集成测试那一步
```

任何一步红就退出码非 0，并指名是哪一步。**别带着红的步骤去 build 镜像。**
「前端构建」这一步偶发过一次原因未明的失败，所以给它一次重试：重试成功会打 `⚠` 并留下日志路径（偶发可容忍，但不许无声）；重试仍失败则把输出末尾打出来。

部署动作是手工 `docker compose build`，没有 PR 合并那道门——所以构建前先问一句 CI 绿了没：

```bash
bash scripts/prebuild-check.sh   # 退出码 0 才该继续构建
```

它按 `origin` 上的 sha 查 GitHub 检查结论：工作区脏 / 还没 push / CI 在跑 → 2（并说明原因），CI 不全绿 → 1，全绿 → 0。
匿名配额只有 60 次/小时，被限流时设 `GITHUB_TOKEN=<pat>` 再跑。

部署走一条命令，别手敲 compose——门禁能被随手跳过时，赶时间就一定会被跳过：

```bash
bash scripts/deploy.sh                        # 前置检查 → 构建 → 起容器 → 冒烟 → 留痕
bash scripts/deploy.sh --force "<理由>"       # 显式绕过，会大声打出来并记进日志
```

冒烟核的是响应而不只是状态码（SPA 兜底会把未知路径也回成 200）：就绪探针响应体要是 `Healthy`、
验证码开关仍开着（`/auth/captcha` 回 `image/gif`）、无令牌访问业务端点回 401、生产 `/openapi/v1.json` 回 404、
首页有 `id="app"`、启动日志零 `ERR/FTL`。每次结果追加到 `logs/deploy.log`（已 gitignore，本机部署史）。

CI（`.github/workflows/ci.yml`）跑的就是这同一条命令——门禁只有一份定义，不在 yml 里另起一套步骤。
它起一个 `postgres:17-alpine` 服务容器并把 `PANSHI_TEST_CONN` 指向一个**空库**，于是每次推送都顺带验证「全新库能完整引导」（CodeFirst 建表 → 迁移 → 种子）。本地想复现同样的条件：

```bash
docker exec -i panshi-db psql -U panshi -d postgres -c "CREATE DATABASE panshi_ci OWNER panshi"
PANSHI_TEST_CONN="Host=localhost;Port=5432;Database=panshi_ci;Username=panshi;Password=<.env 里的值>;Pooling=true" bash scripts/verify.sh
docker exec -i panshi-db psql -U panshi -d postgres -c "DROP DATABASE panshi_ci"
```
`--fast` 之外还会在 `panshi-db` 没起时直接报错提示，而不是把集成测试静默跑成一片红。

> 为什么单独强调「前端类型检查」：`vite build` 不做类型检查，所以类型错误可以让构建一路绿着过去。
> 这一轮就撞上过——`@wangeditor/editor-for-vue` 的 `exports` 没暴露类型入口，`vue-tsc` 红了很久，
> 但没人被拦下来。现在它是验收的第 4 步。

## 来源 IP 与限流（安全 P0）

所有「按调用方」的判断（限流分区、登录日志 IP、操作日志 IP）都走同一个入口 `ClientIp.Of` / `ClientIp.PartitionKey`，
而它信不信 `X-Forwarded-For` 由两个配置决定：

| 配置 | 默认 | 说明 |
|---|---|---|
| `Features:TrustForwardedHeaders` | `false` | 直连形态保持 false；反代/负载均衡后面才设 true |
| `Security:TrustedProxies` | `[]` | **设了 true 就必须填**，否则启动直接失败 |

⚠️ 两条硬规则：
1. `TrustForwardedHeaders=true` 时**只认这份名单**——连框架默认的「信任回环」也清掉。本机反代请显式写 `127.0.0.1`。
   否则任何本机进程都能替你声明来源 IP，「受信名单」就成了建议性的、也不可验证。
2. 名单为空 + 开关为真 = **启动失败**（配置期炸一次，胜过运行期默默信了不该信的人）。

另外注意容器/ NAT 拓扑的静默陷阱：`docker compose` 直连发布端口时，容器看到的对端往往是网关地址，
于是 `login 10/min` 实际是「整站每分钟 10 次」——第 11 次登录会把所有人挡在门外。
启动日志会打印当前是哪种形态（`来源 IP 解析：…`），被限流时也会留一条
`429 限流拒绝 policy=… ip=… path=…` 的 Warning，别等到用户报「登录不上」才发现。

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
