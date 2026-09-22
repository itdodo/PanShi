import 'vue-router'

declare module 'vue-router' {
  interface RouteMeta {
    /** 页签 / 面包屑标题 */
    title?: string
    /** iconify 图标名（lucide:xxx） */
    icon?: string
    /** 是否 keep-alive 缓存 */
    keepAlive?: boolean
    /** keep-alive include 值 = 路由 name = 包装组件 name */
    cachedName?: string
    /** 不在侧栏菜单显示（静态页，如个人中心） */
    hideInMenu?: boolean
    /** 不生成页签（强制改密等流程页） */
    hideTabs?: boolean
    /** 免登录 */
    public?: boolean
    /** 访问所需权限码（可选，菜单接口已按角色过滤，一般留空） */
    permission?: string
    menuId?: string
  }
}

export {}
