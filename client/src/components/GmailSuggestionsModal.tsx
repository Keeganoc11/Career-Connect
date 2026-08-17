import { useState } from 'react'
import type { SuggestedNewApplication, SuggestedStatusUpdate } from '../api/types'
import { STATUS_META } from '../lib/status'
import { formatRelative } from '../lib/format'
import { useEscapeKey } from '../lib/useEscapeKey'

interface Props {
  statusUpdates: SuggestedStatusUpdate[]
  newApplications: SuggestedNewApplication[]
  onAcceptStatusUpdate: (suggestion: SuggestedStatusUpdate) => Promise<void>
  onAddNewApplication: (suggestion: SuggestedNewApplication) => void
  onClose: () => void
}

function StatusPill({ status }: { status: SuggestedStatusUpdate['currentStatus'] }) {
  const meta = STATUS_META[status]
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-semibold ring-1 ring-inset ${meta.badge}`}>
      <span className={`size-1.5 rounded-full ${meta.dot}`} aria-hidden />
      {meta.label}
    </span>
  )
}

function EmailMeta({ subject, from, receivedAtUtc }: { subject: string; from: string; receivedAtUtc: string }) {
  return (
    <div className="mt-3 rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
      <span className="font-medium text-slate-600">{subject}</span>
      <span className="mx-1.5">·</span>
      {from}
      <span className="mx-1.5">·</span>
      {formatRelative(receivedAtUtc)}
    </div>
  )
}

function StatusUpdateRow({
  suggestion,
  onAccept,
  onDismiss,
}: {
  suggestion: SuggestedStatusUpdate
  onAccept: () => Promise<void>
  onDismiss: () => void
}) {
  const [applying, setApplying] = useState(false)

  const accept = async () => {
    setApplying(true)
    try {
      await onAccept()
    } finally {
      setApplying(false)
    }
  }

  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-base font-bold text-slate-900">{suggestion.companyName}</div>
          <div className="text-sm text-slate-500">{suggestion.roleTitle}</div>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <StatusPill status={suggestion.currentStatus} />
          <span className="text-slate-400" aria-hidden>
            →
          </span>
          <StatusPill status={suggestion.suggestedStatus} />
        </div>
      </div>

      <p className="mt-3 text-sm leading-relaxed text-slate-700">{suggestion.reasoning}</p>

      <EmailMeta
        subject={suggestion.emailSubject}
        from={suggestion.emailFrom}
        receivedAtUtc={suggestion.emailReceivedAtUtc}
      />

      <div className="mt-3 flex justify-end gap-2">
        <button
          type="button"
          onClick={onDismiss}
          disabled={applying}
          className="rounded-lg px-3 py-1.5 text-sm font-semibold text-slate-500 transition hover:bg-slate-100 disabled:opacity-60"
        >
          Dismiss
        </button>
        <button
          type="button"
          onClick={() => void accept()}
          disabled={applying}
          className="brand-gradient rounded-lg px-4 py-1.5 text-sm font-semibold text-white shadow-sm transition hover:opacity-95 disabled:opacity-60"
        >
          {applying ? 'Applying…' : `Mark as ${STATUS_META[suggestion.suggestedStatus].label}`}
        </button>
      </div>
    </div>
  )
}

function NewApplicationRow({
  suggestion,
  onAdd,
  onDismiss,
}: {
  suggestion: SuggestedNewApplication
  onAdd: () => void
  onDismiss: () => void
}) {
  return (
    <div className="rounded-xl border border-slate-200 bg-white p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <div className="text-base font-bold text-slate-900">{suggestion.companyName}</div>
          <div className="text-sm text-slate-500">{suggestion.roleTitle || 'Role not stated in email'}</div>
        </div>
        <span className="shrink-0 rounded-full bg-brand-50 px-2.5 py-1 text-xs font-semibold text-brand-700 ring-1 ring-inset ring-brand-200">
          New application
        </span>
      </div>

      <p className="mt-3 text-sm leading-relaxed text-slate-700">{suggestion.reasoning}</p>

      <EmailMeta
        subject={suggestion.emailSubject}
        from={suggestion.emailFrom}
        receivedAtUtc={suggestion.emailReceivedAtUtc}
      />

      <div className="mt-3 flex justify-end gap-2">
        <button
          type="button"
          onClick={onDismiss}
          className="rounded-lg px-3 py-1.5 text-sm font-semibold text-slate-500 transition hover:bg-slate-100"
        >
          Dismiss
        </button>
        <button
          type="button"
          onClick={onAdd}
          className="brand-gradient rounded-lg px-4 py-1.5 text-sm font-semibold text-white shadow-sm transition hover:opacity-95"
        >
          Review &amp; add
        </button>
      </div>
    </div>
  )
}

export function GmailSuggestionsModal({
  statusUpdates: initialStatusUpdates,
  newApplications: initialNewApplications,
  onAcceptStatusUpdate,
  onAddNewApplication,
  onClose,
}: Props) {
  const [statusUpdates, setStatusUpdates] = useState(initialStatusUpdates)
  const [newApplications, setNewApplications] = useState(initialNewApplications)
  useEscapeKey(onClose)

  const removeStatusUpdate = (target: SuggestedStatusUpdate) =>
    setStatusUpdates((current) => current.filter((s) => s !== target))

  const removeNewApplication = (target: SuggestedNewApplication) =>
    setNewApplications((current) => current.filter((s) => s !== target))

  const total = statusUpdates.length + newApplications.length

  return (
    <div
      className="fixed inset-0 z-30 flex items-start justify-center overflow-y-auto bg-slate-900/50 p-4 pt-10 backdrop-blur-sm"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Gmail suggestions"
        className="w-full max-w-2xl overflow-hidden rounded-2xl bg-white shadow-2xl"
      >
        <div className="brand-gradient flex items-start justify-between px-7 py-5">
          <div>
            <h2 className="text-xl font-bold text-white">
              {total === 0 ? 'All caught up' : `${total} suggestion${total === 1 ? '' : 's'} found`}
            </h2>
            <p className="mt-0.5 text-sm text-white/80">
              Found in your recent email — review each before applying.
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded-lg p-1.5 text-white/80 transition hover:bg-white/20 hover:text-white"
          >
            ✕
          </button>
        </div>

        <div className="max-h-[70vh] overflow-y-auto p-7">
          {total === 0 ? (
            <p className="py-8 text-center text-base text-slate-500">
              Nothing left to review — you've handled everything from this scan.
            </p>
          ) : (
            <div className="space-y-6">
              {newApplications.length > 0 && (
                <div>
                  <h3 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-400">
                    New applications
                  </h3>
                  <div className="space-y-4">
                    {newApplications.map((suggestion) => (
                      <NewApplicationRow
                        key={`${suggestion.companyName}-${suggestion.emailSubject}`}
                        suggestion={suggestion}
                        onAdd={() => {
                          onAddNewApplication(suggestion)
                          removeNewApplication(suggestion)
                        }}
                        onDismiss={() => removeNewApplication(suggestion)}
                      />
                    ))}
                  </div>
                </div>
              )}

              {statusUpdates.length > 0 && (
                <div>
                  <h3 className="mb-3 text-sm font-bold uppercase tracking-wide text-slate-400">
                    Status updates
                  </h3>
                  <div className="space-y-4">
                    {statusUpdates.map((suggestion) => (
                      <StatusUpdateRow
                        key={`${suggestion.applicationId}-${suggestion.emailSubject}`}
                        suggestion={suggestion}
                        onAccept={async () => {
                          await onAcceptStatusUpdate(suggestion)
                          removeStatusUpdate(suggestion)
                        }}
                        onDismiss={() => removeStatusUpdate(suggestion)}
                      />
                    ))}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
