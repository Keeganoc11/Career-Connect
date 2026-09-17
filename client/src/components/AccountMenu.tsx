import { useRef, useState } from 'react'
import { Download, LogOut, Trash2 } from 'lucide-react'
import { api, auth } from '../api/client'
import { errorMessage } from '../lib/errors'
import { useAsyncAction } from '../lib/useAsyncAction'
import { DeleteAccountDialog } from './DeleteAccountDialog'
import { formatRelative } from '../lib/format'
import { usePlan } from '../lib/planContext'
import { PRO_AVAILABILITY } from '../lib/tiers'
import type { GmailConnection } from '../lib/useGmailConnection'
import { Banner, Button, ConfirmDialog, Popover, toast } from './ui'

interface Props {
  gmail: GmailConnection
  onSignOut: () => void
}

/** A filled dot carrying on/off state, always beside a word that says the same thing. */
function Dot({ on }: { on: boolean }) {
  return (
    <span
      className={`inline-block size-1.5 rounded-full ${on ? 'bg-success' : 'bg-fg-subtle'}`}
      aria-hidden
    />
  )
}

/**
 * The one home for the Gmail integration.
 *
 * It replaces a control that lived in the applications table's toolbar, where
 * an unlabelled ✕ sat beside "Check for updates" and disconnected Gmail on a
 * single click — no confirmation, no acknowledgement, while a green "Gmail
 * connected." banner stayed on screen. Here the state is always legible and
 * disconnecting has to be confirmed.
 */
export function AccountMenu({ gmail, onSignOut }: Props) {
  const { isPro } = usePlan()
  const buttonRef = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)
  const [confirmingDisconnect, setConfirmingDisconnect] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [exportError, setExportError] = useState<string | null>(null)
  const exporting = useAsyncAction()

  const { status } = gmail
  // Three states, not two. Until the first status call resolves we don't know,
  // and saying "not connected" then would offer a reconnect to someone who is
  // already connected.
  const loading = status === null
  const connected = status?.connected === true
  const calendarOn = connected && status.calendarEnabled
  // Connected but without the calendar scope is the one state worth flagging on
  // the button itself: it looks like it's working and silently isn't.
  const needsAttention = connected && !status.calendarEnabled
  const initial = (auth.email ?? '?').trim().charAt(0).toUpperCase()

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        onClick={() => {
          // Reopening re-reads the connection, so the panel can't show a state
          // that changed in another tab.
          if (!open) void gmail.refreshStatus()
          setOpen((v) => !v)
        }}
        aria-label="Account and integrations"
        aria-expanded={open}
        className="relative inline-flex size-8 items-center justify-center rounded-full bg-surface-muted text-xs font-semibold text-fg ring-1 ring-line transition-colors hover:bg-line pointer-coarse:size-10"
      >
        {initial}
        {needsAttention && (
          <span
            className="absolute -right-0.5 -top-0.5 size-2.5 rounded-full bg-warning ring-2 ring-surface"
            aria-hidden
          />
        )}
      </button>

      {open && (
        <Popover anchorRef={buttonRef} align="end" onClose={() => setOpen(false)}>
          <div className="w-72 px-3 py-2">
            <p className="text-xs text-fg-muted">Signed in as</p>
            <p className="truncate text-sm font-medium text-fg">{auth.email}</p>
            <p className="mt-1 text-xs text-fg-muted">{isPro ? 'Pro plan' : 'Free plan'}</p>
          </div>

          <div className="my-1 border-t border-line" role="separator" />

          {/* Gmail is Pro; a Free account has no connection to describe. */}
          {/* w-72 on every block, not just the first: the popover is only as
              wide as its widest child, and one paragraph without a width sent
              it off the side of the screen. */}
          <div className="w-72 px-3 py-2">
            {!isPro ? (
              <>
                <p className="text-sm font-medium text-fg">Pro adds the automated half</p>
                <p className="mt-1 text-xs text-fg-muted">
                  Resume tailoring, honest scoring, cover letters, and Gmail keeping the tracker up
                  to date. {PRO_AVAILABILITY}
                </p>
              </>
            ) : loading ? (
              <p className="text-sm text-fg-muted">Checking your Gmail connection…</p>
            ) : connected ? (
              <>
                <p className="flex items-center gap-1.5 text-sm font-medium text-fg">
                  <Dot on /> Gmail connected
                </p>
                <p className="mt-0.5 truncate text-xs text-fg-muted">{status.connectedEmail}</p>
                <p className="mt-1 text-xs text-fg-muted">
                  Last checked{' '}
                  {status.lastCheckedAtUtc ? formatRelative(status.lastCheckedAtUtc) : 'not yet'}
                </p>

                <p className="mt-3 flex items-center gap-1.5 text-sm font-medium text-fg">
                  <Dot on={calendarOn} /> Calendar sync {calendarOn ? 'on' : 'off'}
                </p>
                <p className="mt-0.5 text-xs text-fg-muted">
                  {calendarOn
                    ? 'Interviews are added to your Google Calendar.'
                    : 'Google can’t widen an existing connection, so this one has to be redone.'}
                </p>
                {!calendarOn && (
                  <div className="mt-2">
                    <Button size="sm" onClick={() => void gmail.connect()}>
                      Reconnect Gmail
                    </Button>
                  </div>
                )}

                <div className="mt-3">
                  <Button
                    size="sm"
                    tone="danger"
                    onClick={() => {
                      setOpen(false)
                      setConfirmingDisconnect(true)
                    }}
                  >
                    Disconnect Gmail…
                  </Button>
                </div>
              </>
            ) : (
              <>
                <p className="flex items-center gap-1.5 text-sm font-medium text-fg">
                  <Dot on={false} /> Gmail not connected
                </p>
                <p className="mt-1 text-xs text-fg-muted">
                  Connect it and Career Connect reads your recent mail for application updates, and
                  can put interviews on your Google Calendar.
                </p>
                <div className="mt-2">
                  <Button size="sm" onClick={() => void gmail.connect()}>
                    Connect Gmail
                  </Button>
                </div>
              </>
            )}
          </div>

          <div className="my-1 border-t border-line" role="separator" />

          <div className="w-72 px-3 py-2">
            <p className="text-sm font-medium text-fg">Your data</p>
            <p className="mt-0.5 text-xs text-fg-muted">
              Everything in this account, as one file.
            </p>
            <div className="mt-2">
              <Button
                size="sm"
                icon={<Download className="size-4" aria-hidden />}
                loading={exporting.busy}
                onClick={() =>
                  void exporting.run(async () => {
                    setExportError(null)
                    try {
                      await api.exportAccount()
                      toast.success('Your data is downloading.')
                    } catch (e) {
                      setExportError(errorMessage(e))
                    }
                  })
                }
              >
                Download my data
              </Button>
            </div>
            {exportError && <Banner>{exportError}</Banner>}
          </div>

          <div className="my-1 border-t border-line" role="separator" />

          <button
            type="button"
            onClick={() => {
              setOpen(false)
              onSignOut()
            }}
            className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-fg transition-colors hover:bg-surface-muted"
          >
            <LogOut className="size-4" aria-hidden />
            Sign out
          </button>

          <button
            type="button"
            onClick={() => {
              setOpen(false)
              setDeleting(true)
            }}
            className="flex w-full items-center gap-2 px-3 py-2 text-left text-sm text-danger transition-colors hover:bg-surface-muted"
          >
            <Trash2 className="size-4" aria-hidden />
            Delete account…
          </button>
        </Popover>
      )}

      {deleting && (
        <DeleteAccountDialog
          onClose={() => setDeleting(false)}
          onDeleted={() => {
            setDeleting(false)
            // The account no longer exists, so there's nothing to sign out of
            // — this just clears the local session and returns to the front.
            onSignOut()
            toast.info('Your account has been deleted.')
          }}
        />
      )}

      {confirmingDisconnect && (
        <ConfirmDialog
          title="Disconnect Gmail?"
          body={`Career Connect will stop checking ${status?.connectedEmail ?? 'your inbox'} for updates, and won't add new interviews to Google Calendar. Interviews already on your calendar stay there.`}
          confirmLabel="Disconnect Gmail"
          busyLabel="Disconnecting…"
          busy={gmail.disconnecting}
          error={gmail.disconnectError}
          onCancel={() => setConfirmingDisconnect(false)}
          onConfirm={() => {
            void gmail.disconnect().then((ok) => {
              if (ok) setConfirmingDisconnect(false)
            })
          }}
        />
      )}
    </>
  )
}
