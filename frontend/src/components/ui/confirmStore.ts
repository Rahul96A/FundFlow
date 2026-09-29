import type { ReactNode } from 'react'
import { create } from 'zustand'

export interface ConfirmOptions {
  title: string
  message: ReactNode
  confirmLabel?: string
  destructive?: boolean
  /** When set, the user must type a reason, returned as `reason`. */
  reasonLabel?: string
}

export interface ConfirmResult {
  confirmed: boolean
  reason?: string
}

interface ConfirmState {
  options: ConfirmOptions | null
  resolve: ((result: ConfirmResult) => void) | null
  ask: (options: ConfirmOptions) => Promise<ConfirmResult>
  settle: (confirmed: boolean, reason?: string) => void
}

export const useConfirmStore = create<ConfirmState>()((set, get) => ({
  options: null,
  resolve: null,
  ask: (options) =>
    new Promise<ConfirmResult>((resolve) => {
      get().resolve?.({ confirmed: false }) // a second request cancels the first
      set({ options, resolve })
    }),
  settle: (confirmed, reason) => {
    const { resolve } = get()
    set({ options: null, resolve: null })
    resolve?.({ confirmed, reason })
  },
}))

/**
 * Imperative confirmation: `const confirm = useConfirm(); const { confirmed } = await confirm({ ... })`.
 * Resolves rather than throws when the user cancels.
 */
export function useConfirm(): (options: ConfirmOptions) => Promise<ConfirmResult> {
  return useConfirmStore((state) => state.ask)
}
