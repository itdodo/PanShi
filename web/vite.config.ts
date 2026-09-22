import { fileURLToPath, URL } from 'node:url'

import vue from '@vitejs/plugin-vue'
import AutoImport from 'unplugin-auto-import/vite'
import Components from 'unplugin-vue-components/vite'
import { NaiveUiResolver } from 'unplugin-vue-components/resolvers'
import Icons from 'unplugin-icons/vite'
import IconsResolver from 'unplugin-icons/resolver'
import { defineConfig } from 'vite'

/** 后端 API 地址（.NET 10 / Panshi.Api） */
const BACKEND = process.env.VITE_PROXY_TARGET ?? 'http://localhost:5125'

export default defineConfig({
  plugins: [
    vue(),
    AutoImport({
      // Vue / Vue Router / Pinia 组合式 API 自动引入（生成 d.ts 供 vue-tsc 使用）
      imports: ['vue', 'vue-router', 'pinia'],
      dts: 'src/types/auto-imports.d.ts',
      // 注意：Naive UI 的 useMessage/useDialog 等组合式 API 一律显式 import，
      // 不放进自动导入，避免与手写 import 产生重名冲突（踩坑红线）
      vueTemplate: false
    }),
    Components({
      // 本地组件 + Naive UI + 图标（<icon-lucide-bell /> 形式，编译期解析）
      dirs: ['src/components'],
      extensions: ['vue'],
      deep: true,
      dts: 'src/types/components.d.ts',
      resolvers: [
        NaiveUiResolver(),
        IconsResolver({ prefix: 'icon', enabledCollections: ['lucide'] })
      ]
    }),
    Icons({ compiler: 'vue3', autoInstall: false })
  ],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  server: {
    host: true,
    port: 5173,
    strictPort: false,
    proxy: {
      '/api': {
        target: BACKEND,
        changeOrigin: true
      },
      // ⚠️ 红线 #9：SignalR 必须 ws:true，否则实时推送静默失效
      '/hubs': {
        target: BACKEND,
        changeOrigin: true,
        ws: true
      }
    }
  },
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    sourcemap: false,
    chunkSizeWarningLimit: 1500
    // ⚠️ 不配置 rollupOptions.output.manualChunks：本项目用 rolldown(Vite 8)，
    // 把 axios/@iconify 等 CJS 依赖与公共 interop 助手拆到不同 chunk 会产生
    // `e is not a function`（chunk 顶层求值失败 → 整个 app 不挂载）。交给 rolldown 自动分包。
  }
})
