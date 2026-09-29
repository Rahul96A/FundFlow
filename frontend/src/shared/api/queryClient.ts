import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query'
import { toast } from '@/components/ui/toastStore'
import { toApiError } from './problem'

declare module '@tanstack/react-query' {
  interface Register {
    mutationMeta: {
      /** Set when the caller renders the error itself (inline in a form) so no global toast is shown. */
      silent?: boolean
    }
  }
}

/** 4xx responses are answers, not glitches: retrying them only delays the message. */
function shouldRetry(failureCount: number, error: unknown): boolean {
  const { status } = toApiError(error)
  if (status >= 400 && status < 500) {
    return false
  }
  return failureCount < 2
}

export function createQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: shouldRetry,
        staleTime: 30_000,
        refetchOnWindowFocus: false,
      },
      mutations: { retry: false },
    },
    queryCache: new QueryCache({
      onError: (error, query) => {
        // Background refetch failures on data already on screen deserve a nudge; first-load failures render inline.
        if (query.state.data !== undefined) {
          toast.error(toApiError(error).message)
        }
      },
    }),
    mutationCache: new MutationCache({
      onError: (error, _variables, _context, mutation) => {
        if (!mutation.meta?.silent) {
          toast.error(toApiError(error).message)
        }
      },
    }),
  })
}

export const queryClient = createQueryClient()
