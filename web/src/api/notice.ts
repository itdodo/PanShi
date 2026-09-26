import { del, get, post, put } from './http'
import type { PageQuery, PagedResult, VoidResult } from './types'

/** 公告（/sys/notice/*，字段与后端 NoticeDto 一致） */
export interface NoticeDto {
  id: string
  title: string
  noticeType: number
  content?: string | null
  status: number
  publishTime?: string | null
  createByName?: string | null
  createTime: string
  version: number
}

/** 后端 NoticeSaveDto：新建/编辑共用，编辑必带 version */
export interface NoticeFormDto {
  title: string
  noticeType: number
  content?: string | null
  status: number
  publishTime?: string | null
  version?: number
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

/** 公告分页（管理端，需 sys:notice:list） */
export function pageNotices(
  query: PageQuery & { title?: string; noticeType?: number | null; status?: number | null }
): Promise<PagedResult<NoticeDto>> {
  return get<PagedResult<NoticeDto>>('/sys/notice/page', query)
}

export function createNotice(dto: NoticeFormDto): Promise<string> {
  return post<string>('/sys/notice', dto)
}

/** 后端是 [HttpPut("{id:long}")]，路径必须带 id */
export function updateNotice(id: string, dto: NoticeFormDto): Promise<VoidResult> {
  return put<VoidResult>(`/sys/notice/${id}`, dto)
}

export function deleteNotice(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/notice/${id}`)
}

/** 最新公告（仅 Status=1） */
export function latestNotices(limit = 5): Promise<NoticeDto[]> {
  return get<NoticeDto[]>('/sys/notice/latest', { limit }, { silent: true })
}

export function pageMyMessages(
  query: PageQuery & { isRead?: boolean; keyword?: string }
): Promise<PagedResult<MessageDto>> {
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

/** 删除本人某条消息（软删）。同为显式动作，失败要冒泡 */
export function deleteMessage(id: string): Promise<VoidResult> {
  return del<VoidResult>(`/sys/message/my/${id}`)
}

/** 清空本人已读消息，返回删除条数 */
export function clearReadMessages(): Promise<number> {
  return del<number>('/sys/message/my/read')
}
