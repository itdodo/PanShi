/**
 * 审批流 DSL（FlowGraph）前端镜像 + 解析工具。
 * 与后端 Panshi.Model.Dtos.FlowGraph 字段一一对应（camelCase JSON）。
 * 说明：结构合法性由后端 FlowGraphValidator 在保存/启用/提交时校验，此处只做读写与轻量遍历。
 */

export interface FlowConditionDto {
  variable: string
  /** lt/le/gt/ge/eq/ne/in/contains */
  op: string
  value: unknown
}

export interface FlowBranchDto {
  name?: string | null
  priority: number
  next: string
  conditions: FlowConditionDto[]
}

export interface FlowApproverDto {
  /** user/role/position/deptLeader/submitterChoice */
  type: string
  userIds?: string[] | null
  roleCodes?: string[] | null
  positionCodes?: string[] | null
  /** company / submitterDept */
  scope?: string | null
  deptId?: number | null
}

export interface FlowNodeDto {
  code: string
  /** start/approval/condition/cc/end */
  type: string
  name?: string | null
  next?: string | null
  /** orSign/countersign/sequential */
  mode?: string | null
  approvers?: FlowApproverDto[] | null
  branches?: FlowBranchDto[] | null
  defaultNext?: string | null
  ccUserIds?: string[] | null
  ccRoleCodes?: string[] | null
}

export interface FlowGraphDto {
  nodes: FlowNodeDto[]
  entry: string
}

const fallback = (): FlowGraphDto => ({
  entry: 'start',
  nodes: [
    { code: 'start', type: 'start', name: '发起', next: 'end' },
    { code: 'end', type: 'end', name: '结束' }
  ]
})

function asStringArray(value: unknown): string[] | undefined {
  if (!Array.isArray(value)) return undefined
  return value.map((v) => String(v ?? ''))
}

/** 宽松解析：非法 JSON / 结构缺失一律回落到「start → end」骨架，页面不崩 */
export function parseGraph(nodeJson?: string | null): FlowGraphDto {
  if (!nodeJson) return fallback()
  let raw: unknown
  try {
    raw = JSON.parse(nodeJson)
  } catch {
    return fallback()
  }
  const obj = (raw ?? {}) as Partial<FlowGraphDto> & Record<string, unknown>
  const nodes = Array.isArray(obj.nodes) ? (obj.nodes as unknown[]).map(normalizeNode) : fallback().nodes
  return {
    entry: typeof obj.entry === 'string' && obj.entry ? obj.entry : 'start',
    nodes
  }
}

function normalizeNode(input: unknown): FlowNodeDto {
  const n = (input ?? {}) as Record<string, unknown>
  const node: FlowNodeDto = {
    code: String(n.code ?? ''),
    type: String(n.type ?? 'approval'),
    name: (n.name as string) ?? null,
    next: (n.next as string) ?? null,
    mode: (n.mode as string) ?? null,
    defaultNext: (n.defaultNext as string) ?? null
  }
  const approvers = asArray(n.approvers)
  if (approvers.length) {
    node.approvers = approvers.map((a) => {
      const item = (a ?? {}) as Record<string, unknown>
      return {
        type: String(item.type ?? 'user'),
        userIds: asStringArray(item.userIds),
        roleCodes: asStringArray(item.roleCodes),
        positionCodes: asStringArray(item.positionCodes),
        scope: (item.scope as string) ?? null,
        deptId: item.deptId === undefined || item.deptId === null ? undefined : Number(item.deptId)
      }
    })
  }
  const branches = asArray(n.branches)
  if (branches.length) {
    node.branches = branches.map((b) => {
      const item = (b ?? {}) as Record<string, unknown>
      return {
        name: (item.name as string) ?? '',
        priority: Number(item.priority ?? 100) || 0,
        next: String(item.next ?? ''),
        conditions: asArray(item.conditions).map((c) => {
          const cond = (c ?? {}) as Record<string, unknown>
          return { variable: String(cond.variable ?? ''), op: String(cond.op ?? 'eq'), value: cond.value ?? '' }
        })
      }
    })
  }
  const ccUsers = asStringArray(n.ccUserIds)
  if (ccUsers) node.ccUserIds = ccUsers
  const ccRoles = asStringArray(n.ccRoleCodes)
  if (ccRoles) node.ccRoleCodes = ccRoles
  return node
}

function asArray(value: unknown): unknown[] {
  return Array.isArray(value) ? value : []
}

/** 从 entry 广度优先排序（设计器/驳回候选按此顺序呈现），不可达节点附在尾部 */
export function orderedNodes(graph: FlowGraphDto): FlowNodeDto[] {
  const byCode = new Map(graph.nodes.map((n) => [n.code, n]))
  const out: FlowNodeDto[] = []
  const seen = new Set<string>()
  const queue: string[] = [graph.entry]
  while (queue.length) {
    const code = queue.shift() as string
    if (seen.has(code)) continue
    seen.add(code)
    const node = byCode.get(code)
    if (!node) continue
    out.push(node)
    for (const next of successorCodes(node)) queue.push(next)
  }
  for (const node of graph.nodes) if (!seen.has(node.code)) out.push(node)
  return out
}

/** 节点的全部后继 code */
export function successorCodes(node: FlowNodeDto): string[] {
  const list: string[] = []
  if (node.next) list.push(node.next)
  if (node.defaultNext) list.push(node.defaultNext)
  for (const b of node.branches ?? []) if (b.next) list.push(b.next)
  return list
}

/** 是否含「发起人自选」审批人规则（业务页提交前据此弹选人） */
export function hasSubmitterChoice(graph: FlowGraphDto): boolean {
  return graph.nodes.some((n) =>
    n.type === 'approval' && (n.approvers ?? []).some((a) => a.type === 'submitterChoice')
  )
}

/**
 * 驳回候选节点：后端约束「目标必须是审批节点或 start」，
 * 故取序列中之前的节点 = 入口 start + 更早的审批节点。
 */
export function returnTargets(nodes: FlowNodeDto[], entry: string): FlowNodeDto[] {
  const seen = new Set<string>()
  const out: FlowNodeDto[] = []
  for (const n of nodes) {
    if (seen.has(n.code)) continue
    if (n.type === 'approval' || n.type === 'start' || n.code === entry) {
      seen.add(n.code)
      out.push(n)
    }
  }
  return out
}

export function stringifyGraph(graph: FlowGraphDto): string {
  return JSON.stringify(cleanGraph(graph), null, 2)
}

/** 序列化前瘦身：空数组/空串不落 JSON，避免后端解析出无意义字段 */
export function cleanGraph(graph: FlowGraphDto): FlowGraphDto {
  return {
    entry: graph.entry || 'start',
    nodes: graph.nodes.map((node) => {
      const out: FlowNodeDto = { code: node.code, type: node.type }
      if (node.name) out.name = node.name
      if (node.next) out.next = node.next
      if (node.type === 'approval') out.mode = node.mode || 'orSign'
      if (node.defaultNext) out.defaultNext = node.defaultNext
      if (node.type === 'approval') {
        const approvers = (node.approvers ?? []).map(cleanApprover).filter((a) => a.type)
        if (approvers.length) out.approvers = approvers
      }
      if (node.type === 'condition') {
        const branches = (node.branches ?? []).map(cleanBranch)
        if (branches.length) out.branches = branches
      }
      if (node.type === 'cc') {
        const users = (node.ccUserIds ?? []).filter(Boolean)
        if (users.length) out.ccUserIds = users
      }
      return out
    })
  }
}

function cleanApprover(a: FlowApproverDto): FlowApproverDto {
  const out: FlowApproverDto = { type: a.type }
  if (a.type === 'user') out.userIds = (a.userIds ?? []).filter(Boolean)
  if (a.type === 'role') out.roleCodes = (a.roleCodes ?? []).filter(Boolean)
  if (a.type === 'position') {
    out.positionCodes = (a.positionCodes ?? []).filter(Boolean)
    out.scope = a.scope || 'company'
  }
  if (a.type === 'deptLeader' && a.deptId && a.deptId > 0) out.deptId = a.deptId
  return out
}

function cleanBranch(b: FlowBranchDto): FlowBranchDto {
  return {
    name: b.name || '',
    priority: Number(b.priority ?? 0) || 0,
    next: b.next || '',
    conditions: (b.conditions ?? [])
      .filter((c) => c.variable)
      .map((c) => ({ variable: c.variable, op: c.op || 'eq', value: normalizeCondValue(c.op, c.value) }))
  }
}

/** in 走数组，数值型比较转 number，其余保留字符串 */
export function normalizeCondValue(op: string, value: unknown): unknown {
  if (op === 'in') {
    if (Array.isArray(value)) return value.map((v) => toScalar(v))
    return String(value ?? '')
      .split(/[,，\s]+/)
      .map((s) => s.trim())
      .filter(Boolean)
      .map((s) => toScalar(s))
  }
  if (Array.isArray(value)) return value.length === 1 ? toScalar(value[0]) : value.map((v) => toScalar(v))
  return toScalar(value)
}

function toScalar(value: unknown): string | number {
  const s = String(value ?? '').trim()
  if (s !== '' && !Number.isNaN(Number(s))) return Number(s)
  return s
}

