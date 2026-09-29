import { create } from 'zustand'

export type ThemeMode = 'light' | 'dark'

const STORAGE_KEY = 'fundflow.ui'

interface Persisted {
  sidebarCollapsed: boolean
  themeMode: ThemeMode
}

function systemMode(): ThemeMode {
  return typeof window !== 'undefined' && window.matchMedia?.('(prefers-color-scheme: dark)').matches
    ? 'dark'
    : 'light'
}

function load(): Persisted {
  const fallback: Persisted = { sidebarCollapsed: false, themeMode: systemMode() }
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY)
    if (!raw) {
      return fallback
    }
    const parsed = JSON.parse(raw) as Partial<Persisted>
    return {
      sidebarCollapsed: parsed.sidebarCollapsed === true,
      themeMode: parsed.themeMode === 'dark' || parsed.themeMode === 'light' ? parsed.themeMode : fallback.themeMode,
    }
  } catch {
    // Storage can be blocked (private mode, policy); the UI still works with defaults.
    return fallback
  }
}

function save(state: Persisted) {
  try {
    window.localStorage.setItem(STORAGE_KEY, JSON.stringify(state))
  } catch {
    // ignore: preferences just will not persist
  }
}

interface UiState extends Persisted {
  mobileNavOpen: boolean
  toggleSidebar: () => void
  toggleTheme: () => void
  setMobileNavOpen: (open: boolean) => void
}

export const useUiStore = create<UiState>()((set, get) => ({
  ...load(),
  mobileNavOpen: false,
  toggleSidebar: () => {
    set({ sidebarCollapsed: !get().sidebarCollapsed })
    save({ sidebarCollapsed: get().sidebarCollapsed, themeMode: get().themeMode })
  },
  toggleTheme: () => {
    set({ themeMode: get().themeMode === 'dark' ? 'light' : 'dark' })
    save({ sidebarCollapsed: get().sidebarCollapsed, themeMode: get().themeMode })
  },
  setMobileNavOpen: (open) => set({ mobileNavOpen: open }),
}))
