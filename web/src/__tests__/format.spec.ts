import { describe, expect, it } from 'vitest'
import { formatDate, formatDateTime, formatQty, shortUserAgent } from '@/utils/format'

/**
 * 数量与 UA 的展示口径。formatQty 去尾 0 是库存列的既有约定
 * （库里 numeric(18,4)，直接显示 6.0000 会让每个数量列都像坏掉）。
 */
describe('formatQty', () => {
  it('去掉无意义的尾 0', () => {
    expect(formatQty(6)).toBe('6')
    expect(formatQty('6.0000')).toBe('6')
    expect(formatQty(2.5)).toBe('2.5')
    expect(formatQty('12.3400')).toBe('12.34')
  })

  it('空值按 0 显示而不是空白', () => {
    expect(formatQty(null)).toBe('0')
    expect(formatQty(undefined)).toBe('0')
    expect(formatQty(0)).toBe('0')
  })

  it('digits 参数可控精度', () => {
    expect(formatQty(1.23456, 2)).toBe('1.23')
    expect(formatQty(1.23456, 4)).toBe('1.2346')
  })
})

describe('formatDateTime / formatDate', () => {
  it('空值统一回 "-"', () => {
    expect(formatDateTime(null)).toBe('-')
    expect(formatDateTime(undefined)).toBe('-')
    expect(formatDateTime('')).toBe('-')
    expect(formatDate(null)).toBe('-')
  })

  it('非法字符串不吐 Invalid Date', () => {
    expect(formatDateTime('not-a-date')).toBe('-')
  })

  it('能格式化 ISO 串并按需截日期', () => {
    expect(formatDateTime('2026-10-08T13:45:30')).toMatch(/^2026-10-08 13:45:30$/)
    expect(formatDate('2026-10-08T13:45:30')).toBe('2026-10-08')
  })
})

describe('shortUserAgent', () => {
  const edge = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Edg/120.0.2210.91'
  const chrome = 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36'

  it('空值给「未知设备」', () => {
    expect(shortUserAgent(null)).toBe('未知设备')
    expect(shortUserAgent(undefined)).toBe('未知设备')
  })

  it('Edge 的 UA 里同时有 Chrome/Safari，必须优先判 Edge（UA 标记是 Edg/）', () => {
    expect(shortUserAgent(edge)).toBe('Edge 120')
  })

  it('Chrome 与 Firefox 各自命中', () => {
    expect(shortUserAgent(chrome)).toBe('Chrome 120')
    expect(shortUserAgent('Mozilla/5.0 (Windows NT 10.0; rv:121.0) Gecko/20100101 Firefox/121.0')).toBe('Firefox 121')
  })

  it('认不出来的 UA 截断而不是整条塞进表格', () => {
    const long = 'SomeBot/1.2 (' + 'x'.repeat(80) + ')'
    const out = shortUserAgent(long)
    expect(out.length).toBeLessThanOrEqual(32)
    expect(out.startsWith('SomeBot/1.2')).toBe(true)
  })
})
