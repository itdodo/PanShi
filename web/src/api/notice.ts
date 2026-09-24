import { get, post } from './http'
import type { PageQuery, PagedResult, VoidResult } from './types'

/** 公告（后端批次 #6 提供 /sys/notice/*，未上线时全部静默失败，不阻塞布局） */
export interface NoticeDto {
  id: string
  title: string
  noticeType?: number
  content?: string | null
  status?: number
  publishTime?: string | null
  createTime: string
}

/** 站内信 */
export interface MessageDto {
  id: string
  title: string
  content?: string | null
  /** 1 系统 / 2 业务 / 3 站内信 */
  msgType: number
  bizType?: string | null
  bizId?: string | null
  isRead: boolean
  readTime?: string | null
  senderId?: string | null
  senderName?: string | null
  createTime: string
}

/** 最新公告（仅 Status=1） */
export function latestNotices(limit = 5): Promise<NoticeDto[]> {
  return get<NoticeDto[]>('/sys/notice/latest', { limit }, { silent: true })
}

export function pageMyMessages(query: PageQuery & { isRead?: boolean }): Promise<PagedResult<MessageDto>> {
  return get<PagedResult<MessageDto>>('/sys/message/my/page', query, { silent: true })
}

export function getUnreadCount(): Promise<number> {
  return get<number>('/sys/message/my/unread-count', undefined, { silent: true })
}

export function markMessageRead(id: string): Promise<VoidResult> {
  return post<VoidResult>(`/sys/message/my/${id}/read`, undefined, { silent: true })
}

export function markAllRead(): Promise<VoidResult> {
  // ⚠️ 后端路由是 [Route("api/v1/sys/message")] + [HttpPost("read-all")]，不带 my/ 段
  //（与 my/page、my/{id}/read 不同）。这里刻意不 silent：它是用户显式点击的动作，
  // 失败必须冒泡提示，否则又会变成「点了没反应也不报错」。
  return post<VoidResult>('/sys/message/read-all')
}
