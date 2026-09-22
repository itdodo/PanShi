import { del, get, post, put } from '../../api/http'
import type { VoidResult } from '../../api/types'

/** 部门树（/sys/dept/tree）契约预声明 */
export interface DeptTreeNode {
  id: string
  parentId: string | null
  deptName: string
  code?: string | null
  leaderUserId?: string | null
  leaderName?: string | null
  phone?: string | null
  sort: number
  status: number
  children?: DeptTreeNode[] | null
}

export interface DeptFormDto {
  id?: string
  parentId: string | null
  deptName: string
  code?: string | null
  leaderUserId?: string | null
  phone?: string | null
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

export function updateDept(dto: DeptFormDto): Promise<VoidResult> {
  return put<VoidResult>('/sys/dept', dto)
}

export function deleteDept(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/dept/${id}`)
}
