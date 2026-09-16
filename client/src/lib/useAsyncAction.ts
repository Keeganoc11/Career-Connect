import { useCallback, useEffect, useRef, useState } from 'react'
import { errorMessage } from './errors'

export interface AsyncAction {
  /** True while the action is in flight — drives `loading` on the button that started it. */
  busy: boolean
  /** The failure message, or null. Cleared automatically when the action is retried. */
  error: string | null
  /** Resolves true when the action finished without throwing, so callers can close on success only. */
  run: (action: () => Promise<void>) => Promise<boolean>
  clearError: () => void
}

/**
 * One in-flight async action, with its own busy flag and error message.
 *
 * Roughly fifteen call sites hand-rolled this pair of useStates, and most of
 * them forgot to clear the previous error when retrying — so a failure stayed
 * on screen through the next successful attempt.
 */
export function useAsyncAction(): AsyncAction {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const alive = useRef(true)

  // Assigned on mount rather than at declaration: StrictMode mounts twice, and
  // the first cleanup would otherwise leave this false for the real mount.
  useEffect(() => {
    alive.current = true
    return () => {
      alive.current = false
    }
  }, [])

  const run = useCallback(async (action: () => Promise<void>) => {
    setBusy(true)
    setError(null)
    try {
      await action()
      return true
    } catch (e) {
      // The surface that started this stays open on failure, so the message
      // has somewhere to land next to the control the user actually pressed.
      if (alive.current) setError(errorMessage(e))
      return false
    } finally {
      if (alive.current) setBusy(false)
    }
  }, [])

  const clearError = useCallback(() => setError(null), [])

  return { busy, error, run, clearError }
}
