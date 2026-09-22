import type { Directive, DirectiveBinding } from 'vue'
import { useUserStore } from '@/stores/user'

type CodeValue = string | string[] | null | undefined

/** 命令式判定（渲染函数/表格 actions 里用） */
export function hasPerm(code: CodeValue): boolean {
  return useUserStore().hasPermission(code ?? undefined)
}

/**
 * v-permission="'sys:user:add'" / v-permission="['sys:user:add','sys:user:edit']"
 * 无权限时元素级移除（admin 的 permissions 已含全量，无需特判）。
 */
function applyDirective(el: HTMLElement, binding: DirectiveBinding<CodeValue>): void {
  if (hasPerm(binding.value)) return
  const parent = el.parentNode as (Node & { removeChild: (c: Node) => void }) | null
  if (parent) parent.removeChild(el)
  else el.style.display = 'none'
}

export const permissionDirective: Directive<HTMLElement, CodeValue> = {
  mounted: applyDirective,
  updated(el, binding) {
    if (binding.value === binding.oldValue) return
    applyDirective(el, binding)
  }
}
