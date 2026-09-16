import { useState, type ReactNode } from 'react'
import { ArrowRight, RefreshCw } from 'lucide-react'
import type {
  AutoApplied,
  InterviewKind,
  SuggestedNewApplication,
  SuggestedStatusUpdate,
} from '../api/types'
import { STATUS_LABELS } from '../lib/status'
import { formatRelative, fromDateTimeLocalValue, toDateTimeLocalValue } from '../lib/format'
import { useAsyncAction } from '../lib/useAsyncAction'
import type { GmailConnection } from '../lib/useGmailConnection'
import { Button, Card, Checkbox, EmptyState, Input, Modal, StatusBadge } from './ui'

interface Props {
  gmail: GmailConnection
  /** Opens the Add application form pre-filled from a suggestion. */
  onAddNewApplication: (suggestion: SuggestedNewApplication) => void
  onClose: () => void
}

function Transition({
  from,
  to,
}: {
  from: SuggestedStatusUpdate['currentStatus']
  to: SuggestedStatusUpdate['currentStatus']
}) {
  return (
    <div className="flex shrink-0 items-center gap-1.5">
      <StatusBadge status={from} />
      <ArrowRight className="size-3.5 text-fg-subtle" aria-hidden />
      <StatusBadge status={to} />
    </div>
  )
}

function EmailMeta({
  subject,
  from,
  receivedAtUtc,
}: {
  subject: string
  from: string
  receivedAtUtc: string
}) {
  return (
    <p className="mt-3 rounded-control bg-surface-muted px-3 py-2 text-xs text-fg-muted">
      <span className="font-medium text-fg">{subject}</span> · {from} ·{' '}
      {formatRelative(receivedAtUtc)}
    </p>
  )
}

function StatusUpdateCard({
  suggestion,
  onAccept,
  onDismiss,
}: {
  suggestion: SuggestedStatusUpdate
  onAccept: (interview?: { interviewAtUtc: string; interviewKind: InterviewKind }) => Promise<void>
  onDismiss: () => void
}) {
  const accept = useAsyncAction()

  // Editable, and on by default: the model read this time out of an email, and
  // a misread one books the wrong appointment. Correcting it here is cheaper
  // than fixing it on a calendar afterwards.
  const [scheduleIt, setScheduleIt] = useState(Boolean(suggestion.interviewAtUtc))
  const [slot, setSlot] = useState(() =>
    suggestion.interviewAtUtc ? toDateTimeLocalValue(suggestion.interviewAtUtc) : '',
  )

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm font-medium text-fg">{suggestion.companyName}</p>
          <p className="text-sm text-fg-muted">{suggestion.roleTitle}</p>
        </div>
        <Transition from={suggestion.currentStatus} to={suggestion.suggestedStatus} />
      </div>

      <p className="mt-3 text-sm leading-relaxed text-fg-muted">{suggestion.reasoning}</p>

      <EmailMeta
        subject={suggestion.emailSubject}
        from={suggestion.emailFrom}
        receivedAtUtc={suggestion.emailReceivedAtUtc}
      />

      {suggestion.interviewAtUtc && (
        <div className="mt-3 rounded-control border border-line p-3">
          <Checkbox
            checked={scheduleIt}
            onChange={(event) => setScheduleIt(event.target.checked)}
            label={
              <>
                Also schedule this interview
                <span className="mt-0.5 block text-xs text-fg-muted">
                  Read out of the email — check the time before accepting.
                </span>
              </>
            }
          />
          <div className="mt-2">
            <Input
              type="datetime-local"
              value={slot}
              disabled={!scheduleIt}
              onChange={(event) => setSlot(event.target.value)}
              aria-label="Interview date and time"
            />
          </div>
        </div>
      )}

      {accept.error && <p className="mt-3 text-sm text-danger">{accept.error}</p>}

      <div className="mt-3 flex justify-end gap-2">
        <Button size="sm" onClick={onDismiss} disabled={accept.busy}>
          Dismiss
        </Button>
        <Button
          size="sm"
          loading={accept.busy}
          onClick={() =>
            void accept.run(() =>
              onAccept(
                scheduleIt && slot
                  ? {
                      interviewAtUtc: fromDateTimeLocalValue(slot),
                      interviewKind: suggestion.interviewKind ?? 'Other',
                    }
                  : undefined,
              ),
            )
          }
        >
          {scheduleIt && slot
            ? `Mark as ${STATUS_LABELS[suggestion.suggestedStatus]} & schedule`
            : `Mark as ${STATUS_LABELS[suggestion.suggestedStatus]}`}
        </Button>
      </div>
    </Card>
  )
}

function NewApplicationCard({
  suggestion,
  onAdd,
  onDismiss,
}: {
  suggestion: SuggestedNewApplication
  onAdd: () => void
  onDismiss: () => void
}) {
  return (
    <Card>
      <p className="text-sm font-medium text-fg">{suggestion.companyName}</p>
      <p className="text-sm text-fg-muted">
        {suggestion.roleTitle || 'Role not stated in the email'}
      </p>

      <p className="mt-3 text-sm leading-relaxed text-fg-muted">{suggestion.reasoning}</p>

      <EmailMeta
        subject={suggestion.emailSubject}
        from={suggestion.emailFrom}
        receivedAtUtc={suggestion.emailReceivedAtUtc}
      />

      <div className="mt-3 flex justify-end gap-2">
        <Button size="sm" onClick={onDismiss}>
          Dismiss
        </Button>
        <Button size="sm" onClick={onAdd}>
          Review &amp; add
        </Button>
      </div>
    </Card>
  )
}

function AutoAppliedCard({ confirmation }: { confirmation: AutoApplied }) {
  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm font-medium text-fg">{confirmation.companyName}</p>
          <p className="text-sm text-fg-muted">{confirmation.roleTitle}</p>
        </div>
        <Transition from="Preparing" to="Applied" />
      </div>

      <p className="mt-3 text-sm leading-relaxed text-fg-muted">{confirmation.reasoning}</p>

      <EmailMeta
        subject={confirmation.emailSubject}
        from={confirmation.emailFrom}
        receivedAtUtc={confirmation.emailReceivedAtUtc}
      />
    </Card>
  )
}

function Section({ title, hint, children }: { title: string; hint?: string; children: ReactNode }) {
  return (
    <section>
      <h3 className="text-sm font-semibold text-fg">{title}</h3>
      {hint && <p className="mt-0.5 text-sm text-fg-muted">{hint}</p>}
      <div className="mt-2 space-y-3">{children}</div>
    </section>
  )
}

/**
 * Renamed from "Gmail suggestions" per the glossary, and moved out of the
 * applications table's toolbar into the header. Closing it no longer loses
 * anything: updates stay on the server until they're accepted or dismissed.
 */
export function EmailUpdatesModal({ gmail, onAddNewApplication, onClose }: Props) {
  const { status, updates } = gmail
  const connected = status?.connected === true
  const statusUpdates = updates?.statusUpdates ?? []
  const newApplications = updates?.newApplications ?? []
  const autoApplied = updates?.autoApplied ?? []
  const total = statusUpdates.length + newApplications.length + autoApplied.length

  return (
    <Modal
      title="Email updates"
      description="Found in your recent email — review each one before it's applied."
      error={gmail.checkError}
      onClose={onClose}
      footerStart={
        connected && (
          <span className="text-xs text-fg-muted">
            Last checked{' '}
            {status.lastCheckedAtUtc ? formatRelative(status.lastCheckedAtUtc) : 'not yet'}
          </span>
        )
      }
      footer={
        <>
          {connected && (
            <Button
              icon={<RefreshCw className="size-4" aria-hidden />}
              loading={gmail.checking}
              onClick={() => void gmail.check()}
            >
              Check for updates
            </Button>
          )}
          <Button onClick={onClose}>Close</Button>
        </>
      }
    >
      {!connected ? (
        <EmptyState
          title="Gmail isn't connected"
          description="Connect it and Career Connect reads your recent mail for application updates, and can put interviews on your Google Calendar."
          action={<Button onClick={() => void gmail.connect()}>Connect Gmail</Button>}
        />
      ) : total === 0 ? (
        <EmptyState title="All caught up" description="Nothing is waiting for review." />
      ) : (
        <div className="space-y-6">
          {autoApplied.length > 0 && (
            <Section
              title="Confirmed as applied"
              hint="You were prepping these and the company confirmed they got your application, so they've already moved. Nothing to do."
            >
              {autoApplied.map((confirmation) => (
                <AutoAppliedCard key={confirmation.applicationId} confirmation={confirmation} />
              ))}
            </Section>
          )}

          {newApplications.length > 0 && (
            <Section title="New applications">
              {newApplications.map((suggestion) => (
                <NewApplicationCard
                  key={`${suggestion.companyName}-${suggestion.emailSubject}`}
                  suggestion={suggestion}
                  onAdd={() => onAddNewApplication(suggestion)}
                  onDismiss={() => gmail.dismissNewApplication(suggestion)}
                />
              ))}
            </Section>
          )}

          {statusUpdates.length > 0 && (
            <Section title="Status updates">
              {statusUpdates.map((suggestion) => (
                <StatusUpdateCard
                  key={`${suggestion.applicationId}-${suggestion.emailSubject}`}
                  suggestion={suggestion}
                  onAccept={(interview) => gmail.acceptStatusUpdate(suggestion, interview)}
                  onDismiss={() => gmail.dismissStatusUpdate(suggestion)}
                />
              ))}
            </Section>
          )}
        </div>
      )}
    </Modal>
  )
}
