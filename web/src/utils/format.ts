import dayjs from 'dayjs'
import relativeTime from 'dayjs/plugin/relativeTime'
import 'dayjs/locale/zh-cn'

dayjs.extend(relativeTime)
dayjs.locale('zh-cn')

export const DATE_TIME_FORMAT = 'YYYY-MM-DD HH:mm:ss'

export function formatDateTime(value?: string | Date | null, pattern = DATE_TIME_FORMAT): string {
  if (!value) return '-'
  const d = dayjs(value)
  return d.isValid() ? d.format(pattern) : '-'
}

export function formatDate(value?: string | Date | null): string {
  return formatDateTime(value, 'YYYY-MM-DD')
}

/** 「3 分钟前」相对时间（通知列表用） */
export function fromNow(value?: string | Date | null): string {
  if (!value) return '-'
  const d = dayjs(value)
  return d.isValid() ? d.fromNow() : '-'
}

/** UserAgent 简写（会话列表展示） */
export function shortUserAgent(ua?: string | null): string {
  if (!ua) return '未知设备'
  const edge = /Edg\/(\d+)/i.exec(ua)
  if (edge) return `Edge ${edge[1]}`
  const chrome = /Chrome\/(\d+)/i.exec(ua)
  if (chrome) return `Chrome ${chrome[1]}`
  const firefox = /Firefox\/(\d+)/i.exec(ua)
  if (firefox) return `Firefox ${firefox[1]}`
  const safari = /Version\/(\d+).*Safari/i.exec(ua)
  if (safari) return `Safari ${safari[1]}`
  return ua.slice(0, 32)
}
