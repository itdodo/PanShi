import { del, get, post, put } from '../../api/http'
import type { VoidResult } from '../../api/types'

/**
 * 部门管理（/sys/dept）。字段与后端 DeptDto / DeptSaveDto 一一对齐：
 * 编码是 deptCode（不是 code），负责人有 leader（文本）与 leaderUserId（用户 id）两个字段。
 */
export interface DeptTreeNode {
  id: string
  parentId: string | null
  deptCode: string
  deptName: string
  leader?: string | null
  leaderUserId?: string | null
  sort: number
  status: number
  version: number
  createTime: string
  children?: DeptTreeNode[] | null
}

export interface DeptFormDto {
  parentId: string | null
  deptCode: string
  deptName: string
  leader?: string | null
  leaderUserId?: string | null
  sort: number
  status: number
  version?: number
}

export function getDeptTree(keyword?: string): Promise<DeptTreeNode[]> {
  return get<DeptTreeNode[]>('/sys/dept/tree', keyword ? { keyword } : undefined)
}

export function createDept(dto: DeptFormDto): Promise<string> {
  return post<string>('/sys/dept', dto)
}

/** 后端是 [HttpPut("{id:long}")]，路径必须带 id */
export function updateDept(id: string, dto: DeptFormDto): Promise<VoidResult> {
  return put<VoidResult>(`/sys/dept/${id}`, dto)
}

export function deleteDept(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/dept/${id}`)
}
