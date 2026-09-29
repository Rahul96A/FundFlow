import type { AlertColor } from '@mui/material/Alert'
import { create } from 'zustand'

export interface ToastItem {
  id: number
  severity: AlertColor
  message: string
}

interface ToastState {
  toasts: ToastItem[]
  push: (severity: AlertColor, message: string) => void
  dismiss: (id: number) => void
}

let nextId = 1

export const useToastStore = create<ToastState>()((set) => ({
  toasts: [],
  push: (severity, message) =>
    set((state) => ({
      // A burst of identical errors (retries, list + detail failing together) should read as one toast.
      toasts: state.toasts.some((t) => t.message === message && t.severity === severity)
        ? state.toasts
        : [...state.toasts.slice(-2), { id: nextId++, severity, message }],
    })),
  dismiss: (id) => set((state) => ({ toasts: state.toasts.filter((t) => t.id !== id) })),
}))

/** Callable from anywhere (mutation callbacks, interceptors), not just React components. */
export const toast = {
  success: (message: string) => useToastStore.getState().push('success', message),
  error: (message: string) => useToastStore.getState().push('error', message),
  info: (message: string) => useToastStore.getState().push('info', message),
  warning: (message: string) => useToastStore.getState().push('warning', message),
}
