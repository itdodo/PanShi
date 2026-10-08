import { describe, expect, it } from 'vitest'
import {
  hasSubmitterChoice,
  normalizeCondValue,
  orderedNodes,
  parseGraph,
  returnTargets,
  stringifyGraph,
  successorCodes,
  type FlowNodeDto
} from '@/components/flowGraph'

const node = (over: Partial<FlowNodeDto> & { code: string }): FlowNodeDto => ({ type: 'approval', ...over })

/** 设计器与审批中心全靠这几个纯函数，坏一个就是整页空白或顺序错乱 */
describe('parseGraph 宽松解析', () => {
  it('空/坏 JSON 一律回落到 start→end 骨架，不抛异常', () => {
    for (const input of [null, undefined, '', '{', 'not json', '[]', '{"nodes":"nope"}']) {
      const g = parseGraph(input as string | null)
      expect(g.entry).toBe('entry' in g && typeof g.entry === 'string' ? g.entry : 'start')
      expect(g.nodes.map((n) => n.code)).toEqual(['start', 'end'])
      expect(g.nodes[0].next).toBe('end')
    }
  })

  it('缺 entry 补 start，节点字段缺失补默认（type 默认 approval）', () => {
    const g = parseGraph('{"nodes":[{"code":"a"}]}')
    expect(g.entry).toBe('start')
    expect(g.nodes[0]).toMatchObject({ code: 'a', type: 'approval' })
  })

  it('数字型 id 被统一成字符串（雪花号走 string，不能变 number）', () => {
    const g = parseGraph('{"nodes":[{"code":"a","type":"approval","approvers":[{"type":"user","userIds":[123,456]}]}]}')
    expect(g.nodes[0].approvers?.[0].userIds).toEqual(['123', '456'])
  })
})

describe('orderedNodes / successorCodes', () => {
  const graph = {
    entry: 'start',
    nodes: [
      node({ code: 'end', type: 'end' }),
      node({ code: 'start', type: 'start', next: 'cond' }),
      node({ code: 'cond', type: 'condition', defaultNext: 'b2', branches: [{ name: '', priority: 1, next: 'b1', conditions: [] }] }),
      node({ code: 'b1', next: 'end' }),
      node({ code: 'b2', next: 'end' }),
      node({ code: 'orphan' }) // 不可达
    ]
  }

  it('从入口广度优先，不可达节点排在尾部', () => {
    expect(orderedNodes(graph).map((n) => n.code)).toEqual(['start', 'cond', 'b2', 'b1', 'end', 'orphan'])
  })

  it('后继 = next + defaultNext + 各分支出口', () => {
    const cond = graph.nodes.find((n) => n.code === 'cond')!
    expect(successorCodes(cond)).toEqual(['b2', 'b1'])
    expect(successorCodes(node({ code: 'x' }))).toEqual([])
  })
})

describe('hasSubmitterChoice / returnTargets', () => {
  const nodes = [
    node({ code: 'start', type: 'start', next: 'a' }),
    node({ code: 'a', approvers: [{ type: 'submitterChoice' }] }),
    node({ code: 'cc', type: 'cc', ccUserIds: ['9'] }),
    node({ code: 'end', type: 'end' })
  ]

  it('只有审批节点上的 submitterChoice 才算需要发起人自选', () => {
    expect(hasSubmitterChoice({ entry: 'start', nodes })).toBe(true)
    expect(hasSubmitterChoice({ entry: 'start', nodes: nodes.filter((n) => n.code !== 'a') })).toBe(false)
    // 抄送节点带同名规则也不算
    expect(hasSubmitterChoice({ entry: 'start', nodes: [node({ code: 'c', type: 'cc', approvers: [{ type: 'submitterChoice' }] })] })).toBe(false)
  })

  it('驳回候选只收审批节点与入口', () => {
    const codes = returnTargets(nodes, 'start').map((n) => n.code)
    expect(codes).toEqual(['start', 'a'])
  })
})

describe('normalizeCondValue', () => {
  it('in 支持中英文逗号与空白分隔，并逐项转标量', () => {
    expect(normalizeCondValue('in', '1,2，3 4')).toEqual([1, 2, 3, 4])
    expect(normalizeCondValue('in', ['1', 'x'])).toEqual([1, 'x'])
  })

  it('非 in 时单元素数组收成标量，数字字符串转数字', () => {
    expect(normalizeCondValue('eq', ['7'])).toBe(7)
    expect(normalizeCondValue('gt', '100')).toBe(100)
    expect(normalizeCondValue('contains', 'abc')).toBe('abc')
  })
})

describe('stringifyGraph 瘦身', () => {
  it('空数组/空串不落进 JSON，但审批节点一定带 mode（默认 orSign）', () => {
    const json = stringifyGraph({
      entry: 'start',
      nodes: [node({ code: 'a', name: '', approvers: [], next: 'end' })]
    })
    const parsed = JSON.parse(json)
    expect(parsed.nodes[0]).toEqual({ code: 'a', type: 'approval', next: 'end', mode: 'orSign' })
    expect(json).not.toContain('approvers')
  })
})
