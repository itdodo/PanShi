import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vitest/config'

/**
 * 前端单测只跑 node 环境：钉的全是纯逻辑（DSL 解析、分页排序映射、格式化），
 * 不引 jsdom/happy-dom —— 需要真渲染的几何与交互验证走真机点测，不在这一层假装覆盖。
 * 复用 vite.config 会把 auto-import/组件解析插件一起拖进来，反而更容易漂。
 */
export default defineConfig({
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) }
  },
  test: {
    environment: 'node',
    include: ['src/**/*.spec.ts']
  }
})
