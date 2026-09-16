import { useRef, useState } from 'react'
import { CalendarPlus, Download, MoreHorizontal, Pencil, Trash2 } from 'lucide-react'
import { api } from '../api/client'
import type { Application, GmailConnectionStatus, InterviewEvent, InterviewKind } from '../api/types'
import { INTERVIEW_KINDS } from '../api/types'
import {
  formatDateTime,
  formatUntil,
  fromDateTimeLocalValue,
  toDateTimeLocalValue,
} from '../lib/format'
import { KIND_LABELS } from '../lib/interviews'
import { useAsyncAction } from '../lib/useAsyncAction'
import {
  Badge,
  Button,
  ConfirmDialog,
  EmptyState,
  Field,
  IconButton,
  Input,
  Menu,
  Modal,
  Select,
  Textarea,
  toast,
} from './ui'

interface Props {
  application: Application
  /** Drives the hint about whether scheduling also writes to Google Calendar. */
  gmail: GmailConnectionStatus | null
  onClose: () => void
  /** Interviews changed — the tracker reloads so the row and the agenda agree. */
  onChanged: () => void
}

/** A week out at 10am, local: better than making them clear a stale value. */
function defaultSlot(): string {
  const when = new Date()
  when.setDate(when.getDate() + 7)
  when.setHours(10, 0, 0, 0)
  return toDateTimeLocalValue(when.toISOString())
}

export function InterviewsModal({ application, gmail, onClose, onChanged }: Props) {
  const [interviews, setInterviews] = useState<InterviewEvent[]>(application.interviews)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [slot, setSlot] = useState(defaultSlot)
  const [kind, setKind] = useState<InterviewKind>('PhoneScreen')
  const [notes, setNotes] = useState('')
  const [deleteTarget, setDeleteTarget] = useState<InterviewEvent | null>(null)

  const save = useAsyncAction()
  const remove = useAsyncAction()
  const download = useAsyncAction()

  const calendarOn = gmail?.connected === true && gmail.calendarEnabled

  const resetForm = () => {
    setEditingId(null)
    setSlot(defaultSlot())
    setKind('PhoneScreen')
    setNotes('')
  }

  const startEditing = (interview: InterviewEvent) => {
    setEditingId(interview.id)
    setSlot(toDateTimeLocalValue(interview.scheduledAtUtc))
    setKind(interview.kind)
    setNotes(interview.notes ?? '')
  }

  const submit = () =>
    save.run(async () => {
      const input = {
        scheduledAtUtc: fromDateTimeLocalValue(slot),
        kind,
        notes: notes.trim() || undefined,
      }

      const saved = editingId
        ? await api.updateInterview(editingId, input)
        : await api.createInterview(application.id, input)

      setInterviews((current) =>
        [...current.filter((i) => i.id !== saved.id), saved].sort((a, b) =>
          a.scheduledAtUtc.localeCompare(b.scheduledAtUtc),
        ),
      )
      const wasEditing = editingId !== null
      resetForm()
      onChanged()
      toast.success(
        wasEditing
          ? 'Interview updated.'
          : saved.onCalendar
            ? 'Interview scheduled · added to Google Calendar.'
            : 'Interview scheduled.',
      )
    })

  const downloadIcs = (interview: InterviewEvent) =>
    void download.run(async () => {
      // Fetched rather than linked: the endpoint needs the bearer token, which
      // a plain <a download> can't send — the browser would save a 401 instead.
      const ics = await api.getInterviewIcs(interview.id)
      const url = URL.createObjectURL(new Blob([ics], { type: 'text/calendar' }))
      const link = document.createElement('a')
      link.href = url
      link.download = `${application.companyName.replace(/[^\w-]+/g, '-')}-interview.ics`
      link.click()
      URL.revokeObjectURL(url)
    })

  const confirmDelete = () =>
    void remove.run(async () => {
      if (!deleteTarget) return
      await api.deleteInterview(deleteTarget.id)
      setInterviews((current) => current.filter((i) => i.id !== deleteTarget.id))
      if (editingId === deleteTarget.id) resetForm()
      setDeleteTarget(null)
      onChanged()
      toast.success('Interview deleted.')
    })

  // Unsaved input is anything typed into the form that isn't on the server yet.
  const dirty = editingId !== null || notes.trim().length > 0

  return (
    <>
      <Modal
        title="Interviews"
        description={`${application.companyName} · ${application.roleTitle}`}
        error={save.error ?? remove.error ?? download.error}
        busy={save.busy}
        dirty={dirty}
        onClose={onClose}
        footer={
          <>
            <Button onClick={onClose} disabled={save.busy}>
              Close
            </Button>
            <Button
              variant="primary"
              icon={<CalendarPlus className="size-4" aria-hidden />}
              loading={save.busy}
              onClick={() => void submit()}
            >
              {editingId ? 'Save changes' : 'Schedule interview'}
            </Button>
          </>
        }
      >
        <div className="space-y-5">
          {interviews.length === 0 ? (
            <EmptyState
              title="No interviews scheduled"
              description="Add one below and it shows up on your agenda."
            />
          ) : (
            <ul className="space-y-2">
              {interviews.map((interview) => (
                <InterviewRow
                  key={interview.id}
                  interview={interview}
                  onEdit={() => startEditing(interview)}
                  onDownload={() => downloadIcs(interview)}
                  onDelete={() => setDeleteTarget(interview)}
                />
              ))}
            </ul>
          )}

          <div className="space-y-4 border-t border-line pt-5">
            <h3 className="text-sm font-semibold text-fg">
              {editingId ? 'Edit interview' : 'Schedule an interview'}
            </h3>

            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="When">
                {(props) => (
                  <Input
                    {...props}
                    type="datetime-local"
                    value={slot}
                    onChange={(event) => setSlot(event.target.value)}
                  />
                )}
              </Field>
              <Field label="Kind">
                {(props) => (
                  <Select
                    {...props}
                    value={kind}
                    onChange={(event) => setKind(event.target.value as InterviewKind)}
                  >
                    {INTERVIEW_KINDS.map((k) => (
                      <option key={k} value={k}>
                        {KIND_LABELS[k]}
                      </option>
                    ))}
                  </Select>
                )}
              </Field>
            </div>

            <Field
              label="Notes"
              hint={
                calendarOn
                  ? 'Interviews you schedule are added to your Google Calendar.'
                  : 'Calendar sync is off — download the .ics to add one to your calendar.'
              }
            >
              {(props) => (
                <Textarea
                  {...props}
                  rows={3}
                  placeholder="Interviewers, video link, what to prepare…"
                  value={notes}
                  onChange={(event) => setNotes(event.target.value)}
                />
              )}
            </Field>

            {editingId && (
              <Button size="sm" onClick={resetForm}>
                Cancel edit
              </Button>
            )}
          </div>
        </div>
      </Modal>

      {deleteTarget && (
        <ConfirmDialog
          title="Delete interview?"
          body={
            `The ${KIND_LABELS[deleteTarget.kind].toLowerCase()} on ` +
            `${formatDateTime(deleteTarget.scheduledAtUtc)} will be removed.` +
            (deleteTarget.onCalendar ? ' It’s also removed from your Google Calendar.' : '')
          }
          confirmLabel="Delete interview"
          busyLabel="Deleting…"
          busy={remove.busy}
          error={remove.error}
          onCancel={() => setDeleteTarget(null)}
          onConfirm={confirmDelete}
        />
      )}
    </>
  )
}

function InterviewRow({
  interview,
  onEdit,
  onDownload,
  onDelete,
}: {
  interview: InterviewEvent
  onEdit: () => void
  onDownload: () => void
  onDelete: () => void
}) {
  const menuRef = useRef<HTMLButtonElement>(null)
  const [menuOpen, setMenuOpen] = useState(false)
  const past = new Date(interview.scheduledAtUtc) < new Date()

  return (
    <li className="flex items-start justify-between gap-3 rounded-control border border-line px-3.5 py-3">
      <div className="min-w-0">
        <p className={`text-sm font-medium ${past ? 'text-fg-muted' : 'text-fg'}`}>
          {KIND_LABELS[interview.kind]} · {formatDateTime(interview.scheduledAtUtc)}
        </p>
        <p className="mt-0.5 text-xs text-fg-muted">
          {past ? 'Already happened' : formatUntil(interview.scheduledAtUtc)}
          {interview.source !== 'Manual' && ' · found in your email'}
        </p>
        {interview.onCalendar && (
          <span className="mt-1.5 inline-block">
            <Badge emphasis="interview">On Google Calendar</Badge>
          </span>
        )}
        {interview.notes && (
          <p className="mt-1.5 whitespace-pre-wrap text-sm text-fg-muted">{interview.notes}</p>
        )}
      </div>

      <div className="shrink-0">
        <IconButton
          ref={menuRef}
          label={`Actions for this ${KIND_LABELS[interview.kind].toLowerCase()}`}
          icon={<MoreHorizontal className="size-4" aria-hidden />}
          onClick={() => setMenuOpen((open) => !open)}
        />
        {menuOpen && (
          <Menu
            anchorRef={menuRef}
            label="Interview actions"
            onClose={() => setMenuOpen(false)}
            items={[
              {
                key: 'edit',
                label: 'Edit',
                icon: <Pencil className="size-4" aria-hidden />,
                onSelect: onEdit,
              },
              {
                key: 'ics',
                label: 'Download .ics',
                icon: <Download className="size-4" aria-hidden />,
                onSelect: onDownload,
              },
              {
                key: 'delete',
                label: 'Delete interview',
                icon: <Trash2 className="size-4" aria-hidden />,
                destructive: true,
                onSelect: onDelete,
              },
            ]}
          />
        )}
      </div>
    </li>
  )
}
