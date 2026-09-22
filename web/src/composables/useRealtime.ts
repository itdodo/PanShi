import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { useNoticeStore } from '@/stores/notice'
import { notification } from '@/utils/feedback'
import { kickToLogin } from '@/utils/session'
import { tokenStore } from '@/utils/token'

/** SignalR 推送载荷（后端 NotifyService 匿名对象，字段均可空） */
export interface NoticePayload {
  title?: string
  content?: string
  msgType?: string
  bizType?: string
  bizId?: string
  time?: string
}

const HUB_URL = '/hubs/notify'

/** 模块级单例连接（蓝图 §5.9：前端 useRealtime 单例连接） */
let connection: HubConnection | null = null
let connecting = false

export function useRealtime() {
  /** 连接失败只告警，绝不抛错阻塞页面（后端批次 #9 未上线时同样安全） */
  async function connect(): Promise<void> {
    if (connection || connecting || !tokenStore.access) return
    connecting = true
    const conn = new HubConnectionBuilder()
      // accessToken 走 query（access_token），由 accessTokenFactory 提供
      .withUrl(HUB_URL, { accessTokenFactory: () => tokenStore.access })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(LogLevel.Warning)
      .build()

    conn.on('notice', (payload?: NoticePayload) => {
      notification.info({
        title: payload?.title || '新通知',
        content: payload?.content ?? undefined,
        duration: 4500
      })
      void useNoticeStore().refreshAll()
    })

    conn.on('force-logout', (payload?: { reason?: string }) => {
      // 提示后 2 秒回登录页，并发去重在 kickToLogin 内实现
      kickToLogin(payload?.reason || '您已被强制下线')
    })

    conn.onreconnected(() => {
      void useNoticeStore().refreshAll()
    })

    conn.onclose(() => {
      if (connection === conn) {
        connection = null
        connecting = false
      }
    })

    try {
      await conn.start()
      connection = conn
    } catch (err) {
      console.warn('[realtime] /hubs/notify 连接失败（实时推送降级为轮询/手动刷新）', err)
      connecting = false
      await conn.stop().catch(() => undefined)
    }
  }

  async function disconnect(): Promise<void> {
    const conn = connection
    connection = null
    connecting = false
    if (conn) await conn.stop().catch(() => undefined)
  }

  function isConnected(): boolean {
    return connection?.state === HubConnectionState.Connected
  }

  return { connect, disconnect, isConnected }
}
