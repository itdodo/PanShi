import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { todoCount as fetchTodoCount } from '@/api/flow'
import { getUnreadCount, latestNotices, pageMyMessages } from '@/api/notice'
import type { MessageDto, NoticeDto } from '@/api/notice'

/**
 * 铃铛/角标数据源：公告 + 站内信 + 待办数。
 * 后端批次 #6/#7 未上线的接口全部静默失败（角标显示 0），不影响布局。
 */
export const useNoticeStore = defineStore('notice', () => {
  const todoCount = ref(0)
  const unreadCount = ref(0)
  const notices = ref<NoticeDto[]>([])
  const messages = ref<MessageDto[]>([])
  const loading = ref(false)
  /** 待办 + 未读：SignalR notice 事件到达时刷新 */
  const badge = computed(() => (todoCount.value || 0) + (unreadCount.value || 0))

  async function refreshCount(): Promise<void> {
    const [todo, unread] = await Promise.allSettled([fetchTodoCount(), getUnreadCount()])
    todoCount.value = todo.status === 'fulfilled' ? Number(todo.value ?? 0) : 0
    unreadCount.value = unread.status === 'fulfilled' ? Number(unread.value ?? 0) : 0
  }

  async function refreshLists(): Promise<void> {
    loading.value = true
    try {
      const [notice, message] = await Promise.allSettled([
        latestNotices(6),
        pageMyMessages({ pageNum: 1, pageSize: 6 })
      ])
      notices.value = notice.status === 'fulfilled' ? notice.value ?? [] : []
      messages.value = message.status === 'fulfilled' ? message.value?.rows ?? [] : []
    } finally {
      loading.value = false
    }
  }

  async function refreshAll(): Promise<void> {
    await Promise.allSettled([refreshCount(), refreshLists()])
  }

  return { todoCount, unreadCount, notices, messages, loading, badge, refreshCount, refreshLists, refreshAll }
})
