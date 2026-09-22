import type { GlobalThemeOverrides } from 'naive-ui'

/** 品牌主色：高级感蓝（#2563eb 附近），亮暗两套共用 */
const PRIMARY = '#2563eb'
const PRIMARY_HOVER = '#3d7bf4'
const PRIMARY_PRESSED = '#1d4ed8'
const PRIMARY_SUPPLY = '#4f86f7'

const FONT = "'Inter', 'PingFang SC', 'Microsoft YaHei', -apple-system, BlinkMacSystemFont, sans-serif"

const commonRadius = {
  borderRadius: '8px',
  borderRadiusSmall: '6px'
}

export const lightThemeOverrides: GlobalThemeOverrides = {
  common: {
    primaryColor: PRIMARY,
    primaryColorHover: PRIMARY_HOVER,
    primaryColorPressed: PRIMARY_PRESSED,
    primaryColorSuppl: PRIMARY_SUPPLY,
    infoColor: '#0ea5e9',
    successColor: '#16a34a',
    warningColor: '#d97706',
    errorColor: '#dc2626',
    fontFamily: FONT,
    ...commonRadius
  },
  Card: {
    borderRadius: '12px',
    paddingMedium: '20px'
  },
  Button: {
    fontWeight: '500',
    borderRadiusMedium: '8px'
  },
  Input: {
    borderRadius: '8px'
  },
  Select: {
    peers: {
      InternalSelection: { borderRadius: '8px' }
    }
  },
  Menu: {
    borderRadius: '8px',
    itemHeight: '42px'
  },
  Tag: {
    borderRadius: '6px'
  },
  DataTable: {
    thFontWeight: '600'
  }
}

export const darkThemeOverrides: GlobalThemeOverrides = {
  common: {
    primaryColor: PRIMARY_HOVER,
    primaryColorHover: PRIMARY_SUPPLY,
    primaryColorPressed: PRIMARY,
    primaryColorSuppl: PRIMARY_SUPPLY,
    infoColor: '#38bdf8',
    successColor: '#22c55e',
    warningColor: '#f59e0b',
    errorColor: '#ef4444',
    fontFamily: FONT,
    ...commonRadius
  },
  Card: {
    borderRadius: '12px',
    color: '#18181c'
  },
  Layout: {
    color: '#101014',
    headerColor: '#16161a',
    siderColor: '#141418'
  },
  Menu: {
    borderRadius: '8px',
    itemHeight: '42px',
    colorCollapsed: 'transparent',
    color: 'transparent'
  },
  DataTable: {
    thColor: '#1d1d22',
    tdColor: '#18181c',
    borderColor: '#26262c'
  },
  Tag: {
    borderRadius: '6px'
  }
}
