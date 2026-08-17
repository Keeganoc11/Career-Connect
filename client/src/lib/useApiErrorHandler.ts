import { useCallback } from 'react'
import { ApiError, auth } from '../api/client'

/** Standard handling for any API failure: log out on 401, otherwise surface a message. */
export function useApiErrorHandler(onLoggedOut: () => void, setError: (message: string) => void) {
  return useCallback(
    (e: unknown) => {
      if (e instanceof ApiError && e.status === 401) {
        auth.clear()
        onLoggedOut()
        return
      }
      setError(e instanceof Error ? e.message : 'Something went wrong.')
    },
    [onLoggedOut, setError],
  )
}
