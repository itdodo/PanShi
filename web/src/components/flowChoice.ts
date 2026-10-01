/**
 * 「提交前要不要让发起人选审批人」的共用探测——流程是绑出来的，不是单据自己的属性，
 * 所以只能按 businessTable 反查绑定→定义→图，判断图里有没有 submitterChoice 节点。
 *
 * 走 silent 原生请求：普通提交人通常没有 workflow:binding:list / workflow:def:list 权限，
 * 探测不到时返回 unknown，由页面退化成「可选选人」而不是弹错误。
 */
import { get } from '@/api/http'
import type { FlowBindingDto, FlowDefDto } from '@/api/flow'
import type { PagedResult } from '@/api/types'
import { hasSubmitterChoice, parseGraph } from './flowGraph'

export type ChoiceNeed = 'yes' | 'no' | 'unknown'

export async function probeSubmitterChoice(businessTable: string): Promise<ChoiceNeed> {
  try {
    const bindings = await get<FlowBindingDto[]>('/sys/flow/binding', undefined, { silent: true })
    const binding = (bindings ?? []).find((b) => b.businessTable === businessTable && b.status === 1)
    if (!binding) return 'no'
    const page = await get<PagedResult<FlowDefDto>>(
      '/sys/flow/def/page',
      { pageNum: 1, pageSize: 200, status: 1, keyword: binding.flowCode },
      { silent: true }
    )
    const def = (page?.rows ?? []).find((d) => d.flowCode === binding.flowCode && d.status === 1)
    if (!def) return 'unknown'
    return hasSubmitterChoice(parseGraph(def.nodeJson)) ? 'yes' : 'no'
  } catch {
    return 'unknown'
  }
}
