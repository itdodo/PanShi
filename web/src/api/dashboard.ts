import { get } from './http'

/** 首页统计卡（/dashboard/stats，后端批次 #6/#9 提供；失败静默，首页显示 0 + 占位） */
export interface DashboardStatsDto {
  todoCount?: number
  unreadCount?: number
  noticeCount?: number
  onlineCount?: number
  userCount?: number
}

export function getDashboardStats(): Promise<DashboardStatsDto> {
  return get<DashboardStatsDto>('/dashboard/stats', undefined, { silent: true })
}
