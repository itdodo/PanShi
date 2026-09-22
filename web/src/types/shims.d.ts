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

/** 菜单图标集合（@iconify-json 的 icons.json 体量大，用轻量声明替代结构推断） */
declare module '@iconify-json/lucide/icons.json' {
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
