import '@testing-library/jest-dom/vitest'
import { configure } from '@testing-library/react'
import { afterEach, vi } from 'vitest'
import { useAuthStore } from '@/app/store/authStore'
import { useToastStore } from '@/components/ui/toastStore'

// Lazy route chunks can take a few seconds to import the first time they are needed.
configure({ asyncUtilTimeout: 8_000 })

// jsdom lacks these browser APIs that MUI and the app rely on.
if (!window.matchMedia) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    value: (query: string) => ({
      matches: false,
      media: query,
      onchange: null,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      addListener: vi.fn(),
      removeListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }),
  })
}

window.scrollTo = vi.fn() as unknown as typeof window.scrollTo

class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}
globalThis.ResizeObserver ??= ResizeObserverStub as unknown as typeof ResizeObserver

afterEach(() => {
  // Global stores must not leak between tests.
  useAuthStore.setState({ status: 'loading', accessToken: null, expiresAt: null, user: null })
  useToastStore.setState({ toasts: [] })
  vi.restoreAllMocks()
})
