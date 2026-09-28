/// <reference types="vite/client" />
/// <reference types="unplugin-icons/types/vue" />

declare module '*.vue' {
  import type { DefineComponent } from 'vue'
  const component: DefineComponent<Record<string, unknown>, Record<string, unknown>, unknown>
  export default component
}

interface ImportMetaEnv {
  readonly VITE_PROXY_TARGET?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}

/** 菜单图标集合（@iconify-json 的 icons.json 体量大，用轻量声明替代结构推断） */declare module '@iconify-json/lucide/icons.json' {
  export interface IconifyIconData {
    body: string
    width?: number
    height?: number
  }
  export interface IconifyCollection {
    prefix: string
    icons: Record<string, IconifyIconData>
    aliases?: Record<string, { parent: string }>
    width?: number
    height?: number
  }
  const collection: IconifyCollection
  export default collection
}

/**
 * wangEditor 的 Vue3 组件包只发了 dist/src/index.d.ts，但它的 package.json exports 没暴露类型入口，
 * vue-tsc 严格按 exports 解析 → TS7016（vite build 不受影响，只有 type-check 红）。
 * 就地声明两个组件，别为了一个上游打包问题去动 moduleResolution。
 */
declare module '@wangeditor/editor-for-vue' {
  import type { DefineComponent } from 'vue'
  export const Editor: DefineComponent<Record<string, unknown>>
  export const Toolbar: DefineComponent<Record<string, unknown>>
}
