import { INSTANCE_STATUS, TASK_STATUS } from '@/api/flow'
import { DOC_STATUS } from '@/api/biz'

/**
 * 审批域枚举 → 展示元数据（标签文案 + Naive NTag type）。
 * 色规：1 warning / 2 success / 3 error / 4 info / 5 default（草稿 0 = default）。
 */
/** NTag / NTimelineItem 通用色（规约：1 warning / 2 success / 3 error / 4 info / 5 default） */
export type TagType = 'default' | 'info' | 'success' | 'warning' | 'error'

export interface EnumMeta {
  label: string
  type: TagType
}

/* -------------------------------- 任务状态 -------------------------------- */
const TASK_STATUS_META: Record<number, EnumMeta> = {
  [TASK_STATUS.Pending]: { label: '待办', type: 'warning' },
  [TASK_STATUS.Agreed]: { label: '同意', type: 'success' },
  [TASK_STATUS.Rejected]: { label: '拒绝', type: 'error' },
  [TASK_STATUS.Transferred]: { label: '转办', type: 'info' },
  [TASK_STATUS.AutoPassed]: { label: '自动通过', type: 'default' },
  [TASK_STATUS.Invalidated]: { label: '已失效', type: 'default' },
  [TASK_STATUS.Waiting]: { label: '等待中', type: 'info' },
  [TASK_STATUS.Returned]: { label: '已驳回', type: 'error' }
}

export function taskStatusMeta(status?: number | null): EnumMeta {
  return TASK_STATUS_META[status ?? -1] ?? { label: status === undefined ? '-' : `未知(${status})`, type: 'default' }
}

/* -------------------------------- 实例状态 -------------------------------- */
const INSTANCE_STATUS_META: Record<number, EnumMeta> = {
  [INSTANCE_STATUS.Running]: { label: '审批中', type: 'warning' },
  [INSTANCE_STATUS.Approved]: { label: '已通过', type: 'success' },
  [INSTANCE_STATUS.Rejected]: { label: '已拒绝', type: 'error' },
  [INSTANCE_STATUS.Withdrawn]: { label: '已撤回', type: 'info' },
  [INSTANCE_STATUS.Voided]: { label: '已作废', type: 'default' }
}

export function instanceStatusMeta(status?: number | null): EnumMeta {
  return INSTANCE_STATUS_META[status ?? -1] ?? { label: '未知', type: 'default' }
}

/* -------------------------------- 单据状态 -------------------------------- */
const DOC_STATUS_META: Record<number, EnumMeta> = {
  [DOC_STATUS.Draft]: { label: '草稿', type: 'default' },
  [DOC_STATUS.Running]: { label: '审批中', type: 'warning' },
  [DOC_STATUS.Approved]: { label: '已通过', type: 'success' },
  [DOC_STATUS.Rejected]: { label: '已拒绝', type: 'error' },
  [DOC_STATUS.Withdrawn]: { label: '已撤回', type: 'info' }
}

export function docStatusMeta(status?: number | null): EnumMeta {
  return DOC_STATUS_META[status ?? -1] ?? { label: '未知', type: 'default' }
}

/* -------------------------------- 流转动作 -------------------------------- */
export interface ActionMeta extends EnumMeta {
  /** NTimelineItem 圆点色（留空则按 type 走主题色） */
  color?: string
}

const ACTION_META: Record<string, ActionMeta> = {
  submit: { label: '提交', type: 'default', color: '#9ca3af' },
  approve: { label: '通过', type: 'success' },
  reject: { label: '拒绝', type: 'error' },
  return: { label: '驳回', type: 'warning' },
  transfer: { label: '转办', type: 'info' },
  addsign: { label: '加签', type: 'default', color: '#8b5cf6' },
  withdraw: { label: '撤回', type: 'default', color: '#9ca3af' },
  cc: { label: '抄送', type: 'info', color: '#06b6d4' },
  auto: { label: '自动通过', type: 'default', color: '#9ca3af' },
  void: { label: '作废', type: 'error' }
}

export function actionMeta(action?: string | null): ActionMeta {
  return ACTION_META[(action ?? '').toLowerCase()] ?? { label: action || '流转', type: 'default' }
}

/* -------------------------------- 节点/模式 -------------------------------- */
const NODE_MODE_LABEL: Record<string, string> = {
  orSign: '或签',
  countersign: '会签',
  sequential: '依次'
}

export function nodeModeLabel(mode?: string | null): string {
  return NODE_MODE_LABEL[mode ?? ''] ?? mode ?? '-'
}

export const NODE_TYPES = [
  { value: 'start', label: '开始' },
  { value: 'approval', label: '审批' },
  { value: 'condition', label: '条件分支' },
  { value: 'cc', label: '抄送' },
  { value: 'end', label: '结束' }
] as const

export function nodeTypeLabel(type?: string | null): string {
  return NODE_TYPES.find((t) => t.value === type)?.label ?? type ?? '-'
}

/* -------------------------------- 业务单据 -------------------------------- */
export type BizTable = 'biz_expense' | 'biz_purchase_request'

const BIZ_TABLES: Record<string, { label: string; route: string }> = {
  biz_expense: { label: '报销单', route: '/biz/expense' },
  biz_purchase_request: { label: '采购申请单', route: '/biz/purchase' }
}

export function bizTableLabel(table?: string | null): string {
  return BIZ_TABLES[table ?? '']?.label ?? table ?? '-'
}

export function bizTableRoute(table?: string | null): string {
  return BIZ_TABLES[table ?? '']?.route ?? '/biz/expense'
}

