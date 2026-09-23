import type { FlowBranchDto, FlowConditionDto, FlowGraphDto, FlowNodeDto } from './flowGraph'

/**
 * 把扁平 FlowGraph（entry + nodes[]，边靠 next / defaultNext / branches[].next）转成
 * 「钉钉式」渲染树：一条纵向 track，元素要么是单节点，要么是条件块（并列分支列）。
 * 分支列各自从 branch.next 铺到「汇聚点 join」，join = 各出口后继链的首个公共节点。
 * 纯函数、无 DOM，便于单测与复用（设计器预览 / 实例高亮）。
 */

export interface FlowSegNode {
  kind: 'node'
  node: FlowNodeDto
}

export interface FlowSegBranch {
  name: string
  conditions: FlowConditionDto[]
  isDefault: boolean
  track: FlowSegment[]
}

export interface FlowSegCondition {
  kind: 'condition'
  node: FlowNodeDto
  branches: FlowSegBranch[]
}

export type FlowSegment = FlowSegNode | FlowSegCondition

export interface FlowTree {
  entry: string
  track: FlowSegment[]
}

/** 主干后继：普通节点走 next；条件节点走 defaultNext（视作穿过条件块的主干），再退回首分支 */
function trunkNext(node: FlowNodeDto): string {
  if (node.next) return node.next
  if (node.type === 'condition') return node.defaultNext || node.branches?.[0]?.next || ''
  return ''
}

/** 从 start 沿主干后继走，返回途经 code 序列（防环） */
function trunkPath(map: Map<string, FlowNodeDto>, start: string): string[] {
  const path: string[] = []
  const seen = new Set<string>()
  let cur = start
  while (cur && !seen.has(cur)) {
    seen.add(cur)
    path.push(cur)
    const node = map.get(cur)
    if (!node) break
    cur = trunkNext(node)
  }
  return path
}

/** 汇聚点：所有分支出口主干路径的第一个公共节点；无公共点返回 null（各列自然收尾） */
function computeJoin(map: Map<string, FlowNodeDto>, exits: string[]): string | null {
  const paths = exits.map((e) => trunkPath(map, e)).filter((p) => p.length)
  if (!paths.length) return null
  const [first, ...rest] = paths
  for (const code of first) {
    if (rest.every((p) => p.includes(code))) return code
  }
  return null
}

function buildTrack(map: Map<string, FlowNodeDto>, start: string, stop: string | null): FlowSegment[] {
  const out: FlowSegment[] = []
  const guard = new Set<string>()
  let cur = start
  while (cur && cur !== stop) {
    if (guard.has(cur)) break
    guard.add(cur)
    const node = map.get(cur)
    if (!node) break
    if (node.type === 'condition') {
      const exits = [...(node.branches ?? []).map((b) => b.next), node.defaultNext].filter(Boolean) as string[]
      const join = computeJoin(map, exits)
      const branches: FlowSegBranch[] = (node.branches ?? []).map((b: FlowBranchDto) => ({
        name: b.name ?? '',
        conditions: b.conditions ?? [],
        isDefault: false,
        track: buildTrack(map, b.next, join)
      }))
      if (node.defaultNext) {
        branches.push({ name: '默认 / 其它情况', conditions: [], isDefault: true, track: buildTrack(map, node.defaultNext, join) })
      }
      out.push({ kind: 'condition', node, branches })
      cur = join ?? ''
    } else {
      out.push({ kind: 'node', node })
      cur = node.next ?? ''
    }
  }
  return out
}

export function buildFlowTree(graph: FlowGraphDto): FlowTree {
  const map = new Map(graph.nodes.map((n) => [n.code, n]))
  return { entry: graph.entry, track: buildTrack(map, graph.entry, null) }
}

/** 遍历树里所有节点（含分支列内），供高亮/统计用 */
export function walkTreeSegments(track: FlowSegment[], visit: (seg: FlowSegment) => void): void {
  for (const seg of track) {
    visit(seg)
    if (seg.kind === 'condition') for (const b of seg.branches) walkTreeSegments(b.track, visit)
  }
}
