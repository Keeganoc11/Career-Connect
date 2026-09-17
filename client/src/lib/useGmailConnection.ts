import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type {
  ApplicationStatus,
  GmailConnectionStatus,
  GmailScanResult,
  InterviewKind,
  SuggestedNewApplication,
  SuggestedStatusUpdate,
} from '../api/types'
import { errorMessage } from './errors'
import { toast } from '../components/ui'

export interface GmailConnection {
  status: GmailConnectionStatus | null
  updates: GmailScanResult | null
  /** Items still needing a decision — drives the header's count badge. */
  pendingCount: number
  checking: boolean
  checkError: string | null
  disconnecting: boolean
  disconnectError: string | null
  refreshStatus: () => Promise<void>
  connect: () => Promise<void>
  disconnect: () => Promise<boolean>
  check: () => Promise<void>
  acceptStatusUpdate: (
    suggestion: SuggestedStatusUpdate,
    interview?: { interviewAtUtc: string; interviewKind: InterviewKind },
  ) => Promise<void>
  dismissStatusUpdate: (suggestion: SuggestedStatusUpdate) => void
  dismissNewApplication: (suggestion: SuggestedNewApplication) => void
  /** Drops a suggestion once the application it proposed actually exists. */
  consumeNewApplication: (suggestion: SuggestedNewApplication) => void
}

/**
 * Everything about the Gmail connection in one place, owned by Workspace
 * rather than by the applications table's toolbar. The connection isn't a
 * property of that table — it feeds the agenda and interview scheduling too —
 * and living there is why disconnecting was a stray ✕ beside a search box.
 *
 * @param onApplicationsChanged Accepting an update changes an application, so
 * the tracker has to refetch.
 * @param enabled False on Free, where every Gmail endpoint answers 402 — asking
 * anyway would spend a request per load to be told no.
 */
export function useGmailConnection(
  onApplicationsChanged: () => void,
  enabled: boolean,
): GmailConnection {
  const [status, setStatus] = useState<GmailConnectionStatus | null>(null)
  const [updates, setUpdates] = useState<GmailScanResult | null>(null)
  const [checking, setChecking] = useState(false)
  const [checkError, setCheckError] = useState<string | null>(null)
  const [disconnecting, setDisconnecting] = useState(false)
  const [disconnectError, setDisconnectError] = useState<string | null>(null)

  const refreshStatus = useCallback(async () => {
    if (!enabled) return
    try {
      const latest = await api.getGmailStatus()
      setStatus(latest)

      // A scheduled background scan found something since we last looked —
      // surface it the same way a manual check's results appear.
      if (latest.hasPendingSuggestions) {
        const pending = await api.getPendingGmailSuggestions()
        if (pending) setUpdates(pending)
      }
    } catch {
      // A dead server shows up across the whole page; don't add noise here.
    }
  }, [enabled])

  useEffect(() => {
    void refreshStatus()
  }, [refreshStatus])

  /**
   * Back from Google's consent screen. Says plainly whether calendar sync came
   * with it: the old flow showed one green "Gmail connected." banner whether or
   * not the calendar scope was granted, so there was no way to tell.
   */
  useEffect(() => {
    if (!enabled) return

    const params = new URLSearchParams(window.location.search)
    const result = params.get('gmail')
    if (!result) return

    window.history.replaceState({}, '', window.location.pathname)

    if (result === 'error') {
      toast.error(params.get('message') || 'Could not connect Gmail.')
      return
    }

    void (async () => {
      try {
        const latest = await api.getGmailStatus()
        setStatus(latest)
        toast.success(
          latest.calendarEnabled
            ? 'Gmail connected · calendar sync is on.'
            : 'Gmail connected · calendar sync is off. Reconnect to allow it.',
        )
      } catch {
        toast.success('Gmail connected.')
      }
    })()
    // Reads the URL exactly once, on mount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const connect = useCallback(async () => {
    try {
      window.location.href = await api.getGmailAuthorizationUrl()
    } catch (e) {
      toast.error(errorMessage(e) ?? 'Could not start connecting Gmail.')
    }
  }, [])

  const disconnect = useCallback(async () => {
    setDisconnecting(true)
    setDisconnectError(null)
    try {
      await api.disconnectGmail()
      await refreshStatus()
      setUpdates(null)
      toast.success('Gmail disconnected.')
      return true
    } catch (e) {
      setDisconnectError(errorMessage(e))
      return false
    } finally {
      setDisconnecting(false)
    }
  }, [refreshStatus])

  const check = useCallback(async () => {
    setChecking(true)
    setCheckError(null)
    try {
      const result = await api.scanGmail()
      await refreshStatus()
      setUpdates(result)
      const found =
        result.statusUpdates.length + result.newApplications.length + result.autoApplied.length
      if (found === 0) {
        toast.info('No new updates found.')
      } else if (result.autoApplied.length > 0) {
        // Clear-cut updates already changed status server-side.
        onApplicationsChanged()
        const count = result.autoApplied.length
        toast.info(
          `${count === 1 ? '1 clear-cut update was' : `${count} clear-cut updates were`} applied for you. Undo any under “Done for you”.`,
        )
      }
    } catch (e) {
      setCheckError(errorMessage(e))
    } finally {
      setChecking(false)
    }
  }, [refreshStatus, onApplicationsChanged])

  const removeStatusUpdate = (suggestion: SuggestedStatusUpdate) =>
    setUpdates(
      (current) =>
        current && {
          ...current,
          statusUpdates: current.statusUpdates.filter((s) => s !== suggestion),
        },
    )

  const removeNewApplication = (suggestion: SuggestedNewApplication) =>
    setUpdates(
      (current) =>
        current && {
          ...current,
          newApplications: current.newApplications.filter((s) => s !== suggestion),
        },
    )

  const acceptStatusUpdate = useCallback(
    async (
      suggestion: SuggestedStatusUpdate,
      interview?: { interviewAtUtc: string; interviewKind: InterviewKind },
    ) => {
      await api.acceptGmailSuggestion(
        suggestion.applicationId,
        suggestion.suggestedStatus as ApplicationStatus,
        interview,
      )
      removeStatusUpdate(suggestion)
      onApplicationsChanged()
      toast.success(
        interview
          ? `${suggestion.companyName} updated · interview scheduled.`
          : `${suggestion.companyName} updated.`,
      )
    },
    [onApplicationsChanged],
  )

  // Hide it immediately, but put it back if the server didn't take the
  // dismissal — otherwise it reappears on the next load with no sign anything
  // failed.
  const dismissStatusUpdate = useCallback((suggestion: SuggestedStatusUpdate) => {
    removeStatusUpdate(suggestion)
    void api
      .dismissGmailStatusUpdate(suggestion.applicationId, suggestion.suggestedStatus)
      .catch((e: unknown) => {
        setUpdates(
          (current) =>
            current && { ...current, statusUpdates: [...current.statusUpdates, suggestion] },
        )
        toast.error(errorMessage(e) ?? 'Could not dismiss that update.')
      })
  }, [])

  const dismissNewApplication = useCallback((suggestion: SuggestedNewApplication) => {
    removeNewApplication(suggestion)
    void api.dismissGmailNewApplication(suggestion.companyName).catch((e: unknown) => {
      setUpdates(
        (current) =>
          current && { ...current, newApplications: [...current.newApplications, suggestion] },
      )
      toast.error(errorMessage(e) ?? 'Could not dismiss that suggestion.')
    })
  }, [])

  const consumeNewApplication = useCallback((suggestion: SuggestedNewApplication) => {
    removeNewApplication(suggestion)
    // Failing quietly is fine: reading the updates also drops any company
    // that's now tracked.
    void api.dismissGmailNewApplication(suggestion.companyName).catch(() => {})
  }, [])

  const pendingCount =
    (updates?.statusUpdates.length ?? 0) + (updates?.newApplications.length ?? 0)

  return {
    status,
    updates,
    pendingCount,
    checking,
    checkError,
    disconnecting,
    disconnectError,
    refreshStatus,
    connect,
    disconnect,
    check,
    acceptStatusUpdate,
    dismissStatusUpdate,
    dismissNewApplication,
    consumeNewApplication,
  }
}
