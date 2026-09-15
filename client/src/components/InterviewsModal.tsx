import { useState } from 'react'
import { api, ApiError } from '../api/client'
import type { Application, InterviewEvent, InterviewKind } from '../api/types'
import { INTERVIEW_KINDS } from '../api/types'
import {
  formatDateTime,
  formatUntil,
  fromDateTimeLocalValue,
  toDateTimeLocalValue,
} from '../lib/format'
import { fieldClass } from '../lib/styles'
import { ModalBackdrop, ModalHeader } from './Modal'

const KIND_LABELS: Record<InterviewKind, string> = {
  PhoneScreen: 'Phone screen',
  Technical: 'Technical',
  Onsite: 'Onsite',
  Final: 'Final round',
  Other: 'Interview',
}

interface Props {
  application: Application
  onClose: () => void
  /** Interviews changed — the tracker reloads so the row and the agenda agree. */
  onChanged: () => void
}

/** Local wall-clock default: a week out at 10am, which beats making them clear a stale value. */
function defaultSlot(): string {
  const when = new Date()
  when.setDate(when.getDate() + 7)
  when.setHours(10, 0, 0, 0)
  return toDateTimeLocalValue(when.toISOString())
}

export function InterviewsModal({ application, onClose, onChanged }: Props) {
  const [interviews, setInterviews] = useState<InterviewEvent[]>(application.interviews)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [slot, setSlot] = useState(defaultSlot)
  const [kind, setKind] = useState<InterviewKind>('PhoneScreen')
  const [notes, setNotes] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

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
    setError(null)
  }

  const save = async () => {
    if (!slot) {
      setError('Pick a date and time first.')
      return
    }

    setSaving(true)
    setError(null)
    try {
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
      resetForm()
      onChanged()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Something went wrong.')
    } finally {
      setSaving(false)
    }
  }

  const download = async (interview: InterviewEvent) => {
    setError(null)
    try {
      const ics = await api.getInterviewIcs(interview.id)
      const url = URL.createObjectURL(new Blob([ics], { type: 'text/calendar' }))
      const link = document.createElement('a')
      link.href = url
      link.download = `${application.companyName.replace(/[^\w-]+/g, '-')}-interview.ics`
      link.click()
      URL.revokeObjectURL(url)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Something went wrong.')
    }
  }

  const remove = async (interview: InterviewEvent) => {
    setSaving(true)
    setError(null)
    try {
      await api.deleteInterview(interview.id)
      setInterviews((current) => current.filter((i) => i.id !== interview.id))
      if (editingId === interview.id) resetForm()
      onChanged()
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Something went wrong.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <ModalBackdrop onClose={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Interviews"
        className="w-full max-w-2xl overflow-hidden rounded-2xl bg-white shadow-2xl"
      >
        <ModalHeader
          title="📅 Interviews"
          subtitle={`${application.companyName} — ${application.roleTitle}`}
          onClose={onClose}
        />

        <div className="max-h-[72vh] space-y-6 overflow-y-auto p-7">
          {interviews.length > 0 && (
            <ul className="space-y-2">
              {interviews.map((interview) => {
                const past = new Date(interview.scheduledAtUtc) < new Date()
                return (
                  <li
                    key={interview.id}
                    className={`flex flex-wrap items-center justify-between gap-3 rounded-xl border px-4 py-3 ${
                      past ? 'border-slate-200 bg-slate-50' : 'border-slate-200 bg-white'
                    }`}
                  >
                    <div className="min-w-0">
                      <p className={`font-semibold ${past ? 'text-slate-500' : 'text-slate-900'}`}>
                        {KIND_LABELS[interview.kind]} · {formatDateTime(interview.scheduledAtUtc)}
                      </p>
                      <p className="mt-0.5 text-sm text-slate-500">
                        {past ? 'Already happened' : formatUntil(interview.scheduledAtUtc)}
                        {interview.onCalendar && ' · on your calendar'}
                        {interview.source !== 'Manual' && ' · found in your email'}
                      </p>
                      {interview.notes && (
                        <p className="mt-1 text-sm whitespace-pre-wrap text-slate-600">{interview.notes}</p>
                      )}
                    </div>
                    <div className="flex shrink-0 gap-2">
                      {/* Works with any calendar app, and covers the case where
                          Google calendar sync was never connected. */}
                      <button
                        type="button"
                        onClick={() => void download(interview)}
                        className="rounded-lg px-3 py-1.5 text-sm font-semibold text-slate-600 transition hover:bg-slate-100"
                      >
                        .ics
                      </button>
                      <button
                        type="button"
                        onClick={() => startEditing(interview)}
                        className="rounded-lg px-3 py-1.5 text-sm font-semibold text-slate-600 transition hover:bg-slate-100"
                      >
                        Edit
                      </button>
                      <button
                        type="button"
                        onClick={() => void remove(interview)}
                        disabled={saving}
                        className="rounded-lg px-3 py-1.5 text-sm font-semibold text-rose-600 transition hover:bg-rose-50 disabled:opacity-60"
                      >
                        Remove
                      </button>
                    </div>
                  </li>
                )
              })}
            </ul>
          )}

          <div className="space-y-4 rounded-2xl bg-slate-50 p-5">
            <h3 className="font-bold text-slate-900">
              {editingId ? 'Edit interview' : 'Schedule an interview'}
            </h3>

            <div className="grid gap-4 sm:grid-cols-2">
              <label className="block">
                <span className="mb-1.5 block text-sm font-semibold text-slate-700">When</span>
                <input
                  type="datetime-local"
                  value={slot}
                  onChange={(event) => setSlot(event.target.value)}
                  className={fieldClass}
                />
              </label>
              <label className="block">
                <span className="mb-1.5 block text-sm font-semibold text-slate-700">Kind</span>
                <select
                  value={kind}
                  onChange={(event) => setKind(event.target.value as InterviewKind)}
                  className={fieldClass}
                >
                  {INTERVIEW_KINDS.map((k) => (
                    <option key={k} value={k}>
                      {KIND_LABELS[k]}
                    </option>
                  ))}
                </select>
              </label>
            </div>

            <label className="block">
              <span className="mb-1.5 block text-sm font-semibold text-slate-700">Notes</span>
              <textarea
                value={notes}
                onChange={(event) => setNotes(event.target.value)}
                rows={3}
                placeholder="Interviewers, video link, what to prepare…"
                className={fieldClass}
              />
            </label>

            {error && (
              <p className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-2.5 text-sm text-rose-700">
                {error}
              </p>
            )}

            <div className="flex items-center gap-3">
              <button
                type="button"
                onClick={() => void save()}
                disabled={saving}
                className="brand-gradient rounded-xl px-5 py-2.5 text-sm font-semibold text-white shadow-lg shadow-brand-600/25 transition hover:opacity-95 disabled:opacity-60"
              >
                {saving ? 'Saving…' : editingId ? 'Save changes' : 'Schedule it'}
              </button>
              {editingId && (
                <button
                  type="button"
                  onClick={resetForm}
                  className="rounded-xl border border-slate-200 px-4 py-2.5 text-sm font-semibold text-slate-600 transition hover:bg-slate-50"
                >
                  Cancel
                </button>
              )}
            </div>
          </div>
        </div>
      </div>
    </ModalBackdrop>
  )
}
