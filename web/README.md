# 磐石管理底座 · 前端（web/）

Vue 3.5 + TypeScript 5（strict）+ Vite 8 + **Naive UI** + Pinia 3 + vue-router 4 + axios + @microsoft/signalr + dayjs。

## 启动

```bash
cd web
npm install          # 依赖走 registry（当前 npmmirror）
npm run dev          # http://localhost:5173，/api 与 /hubs(ws) 代理到 http://localhost:5125
npm run build        # 产物 dist/
npm run type-check   # vue-tsc --noEmit
```

后端需先起（`.NET 10`，默认 `http://localhost:5125`，PostgreSQL 由根目录 `docker-compose.yml` 提供）。
代理地址可用环境变量覆盖：`VITE_PROXY_TARGET=http://10.0.0.9:5125 npm run dev`。

登录：`admin` / 部署方设置的初始密码（**前端不预填账号密码**）。
验证码可用后端参数 `sys.captcha.enabled=0` 关闭，关闭后验证码留空即可。

## 目录

```
src/
├── api/          按域分文件：http.ts(封装) auth/menu/notice/flow/dashboard + system/*
├── components/   AppIcon(图标) AppProviders(主题+全局 provider)
├── composables/  usePageList(列表样板) useRealtime(SignalR 单例)
├── directives/   v-permission（元素级移除）
├── layouts/      default/(SideMenu + HeaderBar + TabsBar + NoticeBell + UserActions)
├── router/       index.ts(守卫) routes.ts(静态路由) views.ts(component 字符串 → views 文件)
├── stores/       user / permission / tabs / notice
├── styles/       global.css(滚动条等) theme.ts(亮暗主题覆盖，主色 #2563eb)
├── utils/        http 配套：token/session/feedback/menuIcon/format/themeState
└── views/        login/ dashboard/ system/profile/ common/(强制改密、建设中占位) 403.vue 404.vue
```

## 关键约定（后续批次必读）

1. **响应包**：`{code,msg,data}`；拦截器已拆包，业务函数直接拿到 `data`。`code!==0` 抛 `BizError` 并弹全局提示；
   传 `{ silent: true }` 可静默（未上线接口/后台轮询默认都加了）。401 走**单飞刷新**并自动重放一次，
   刷新失败统一 `kickToLogin()`（提示 + 2 秒跳转 + 并发去重）。
2. **动态路由**：只有 `menuType=2` 注册路由，`component` 字符串（如 `system/user/index`）→ `src/views/system/user/index.vue`；
   找不到文件渲染 `views/common/Developing.vue`，不白屏。无菜单/菜单接口失败 → `/403` 友好页。
3. **keep-alive**：`include` 匹配的是**组件 name**，所以页面组件必须经 `router/views.ts` 的 `namedView()` 包装
   （路由 name === 组件 name === 页签 cachedName）。刷新页签用「含 path 的 key」，纯数字会跨组件撞缓存。
4. **自动引入**：`ref/computed/watch/useRoute/useRouter/defineStore/storeToRefs` 免 import（见 `src/types/auto-imports.d.ts`，
   由 vite 插件生成，需先跑一次 `npm run dev` 或 `npm run build`）；**Naive UI 组件**在模板里免 import，
   但 `useMessage/useDialog/useNotification` 等组合式 API **一律显式 import**（未纳入自动导入，避免重名冲突）。
5. **setup 之外**要用提示：`import { message, dialog, notification } from '@/utils/feedback'`（createDiscreteApi），
   组件内部用 `useMessage()`。
6. **图标**：菜单 `icon` 存 `lucide:xxx`，运行时 `utils/menuIcon.ts` 把 lucide 集合 `addCollection` 进
   **`@iconify/vue/offline`**（内网不打 Iconify API），未知图标兜底 `lucide:file-text`（lucide 没有 `document` 这个名字）；
   模板里可直接写编译期图标组件 `<icon-lucide-bell />`。
7. **长内容弹窗**加 `class="dialog-scroll"`（限高 + 内部滚动），全局滚动条已统一 8px 圆角半透明。

## 已实测（后端 http://localhost:5125）

`/auth/login`（admin）、`/auth/captcha`（blob + `X-Captcha-Id` 经代理可见）、`/auth/profile`、`/auth/sessions`、
`/sys/menu/tree/my` 均返回真实数据；`/flow/task/todo-count` 目前 404（前端静默，角标显示 0）。
⚠️ `menu.status` 沿用后端 `EnableStatus`：**0=启用 1=停用**（写反会导致整个侧栏空白）；
`menu.visible=false` 的节点（如「个人中心 /profile」）只登记不显示，前端已跳过其动态路由（静态 `/profile` 已占用该路径）。
