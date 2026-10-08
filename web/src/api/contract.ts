/**
 * 前后端契约的编译期守卫。
 *
 * `schema.d.ts` 由 `npm run api:gen` 从后端 `/openapi/v1.json` 生成（该端点只在 Development 开放）。
 * 这里把 api/*.ts 里手写的 DTO 键集与生成结果逐条对账：后端改了字段名、加了字段、换了类型，
 * `npm run type-check` 就在那一行报错，而不是等页面取到 undefined 才发现。
 * 快照本身有没有过期由 CI 第二个 job 管（起后端重新生成，逐字节比）。
 *
 * 方向性局限（重要）：生成的快照把每个属性都标成可选，所以「手写 → 快照」的可赋值是唯一
 * 成立的方向，反向必然假失败；真正抓改名的是两侧键集的双向 Exclude 对账。
 *
 * 每个 DTO 都把三条判定摊开写：TS 的报错只给「哪一行」，折叠成一个泛型别名的话所有漂移都会
 * 指到同一行、连是哪个 DTO 都看不出来。摊开后报错行就落在出问题的那条判定上。
 */
import type { components } from './schema'
import type { ExpenseDto, PurchaseDto } from './biz'
import type { StockDocDto, StockDocLineDto } from './scm'
import type { UserDto } from './system/user'
import type { PagedResult } from './types'

type S = components['schemas']

/** 断言失败报 "Type 'false' does not satisfy the constraint 'true'"，报错行即漂移所在的那条契约 */
type Assert<T extends true> = T
/** 手写侧多出来的字段（后端根本不返回，页面迟早拿到 undefined） */
type NoExtraKeys<Hand, Snap> = Exclude<keyof Hand, keyof Snap> extends never ? true : false
/** 后端多出来的字段（快照里有、手写类型没有 → 前端看不见这个字段） */
type NoMissingKeys<Hand, Snap> = Exclude<keyof Snap, keyof Hand> extends never ? true : false
/** 逐字段类型核对：嵌套数组与枚举引用会跟着递归核到 */
type AssignableToSnap<Hand, Snap> = Hand extends Snap ? true : false

export type ExpenseDtoContract = [
  Assert<NoExtraKeys<ExpenseDto, S['ExpenseDto']>>,
  Assert<NoMissingKeys<ExpenseDto, S['ExpenseDto']>>,
  Assert<AssignableToSnap<ExpenseDto, S['ExpenseDto']>>
]

export type PurchaseDtoContract = [
  Assert<NoExtraKeys<PurchaseDto, S['PurchaseDto']>>,
  Assert<NoMissingKeys<PurchaseDto, S['PurchaseDto']>>,
  Assert<AssignableToSnap<PurchaseDto, S['PurchaseDto']>>
]

export type StockDocDtoContract = [
  Assert<NoExtraKeys<StockDocDto, S['StockDocDto']>>,
  Assert<NoMissingKeys<StockDocDto, S['StockDocDto']>>,
  Assert<AssignableToSnap<StockDocDto, S['StockDocDto']>>
]

/** lines 嵌在 StockDocDto 里，单独钉一次：整表判定时嵌套层的改名容易被外层判定放过 */
export type StockDocLineDtoContract = [
  Assert<NoExtraKeys<StockDocLineDto, S['StockDocLineDto']>>,
  Assert<NoMissingKeys<StockDocLineDto, S['StockDocLineDto']>>,
  Assert<AssignableToSnap<StockDocLineDto, S['StockDocLineDto']>>
]

export type UserDtoContract = [
  Assert<NoExtraKeys<UserDto, S['UserDto']>>,
  Assert<NoMissingKeys<UserDto, S['UserDto']>>,
  Assert<AssignableToSnap<UserDto, S['UserDto']>>
]

/**
 * 分页信封。total 后端是 int64，经全局 long→string 转换器落地成字符串，
 * 快照因此是 number | string —— 页面统一 Number() 归一（见 usePageList）。
 */
export type PagedResultContract = [
  Assert<NoExtraKeys<PagedResult<ExpenseDto>, S['PagedResultOfExpenseDto']>>,
  Assert<NoMissingKeys<PagedResult<ExpenseDto>, S['PagedResultOfExpenseDto']>>,
  Assert<AssignableToSnap<PagedResult<ExpenseDto>, S['PagedResultOfExpenseDto']>>
]
