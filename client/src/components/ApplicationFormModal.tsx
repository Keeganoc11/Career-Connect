import { useState, type FormEvent } from 'react'
import { STATUSES, type Application, type ApplicationInput } from '../api/types'
import { STATUS_META } from '../lib/status'

interface Props {
  /** null = creating a new application. */
  application: Application | null
  onSave: (input: ApplicationInput) => Promise<void>
  onClose: () => void
}

function todayIso(): string {
  const now = new Date()
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const day = String(now.getDate()).padStart(2, '0')
  return `${now.getFullYear()}-${month}-${day}`
}

const inputClass =
  'w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30'

export function ApplicationFormModal({ application, onSave, onClose }: Props) {
  const isEdit = application !== null
  const [companyName, setCompanyName] = useState(application?.companyName ?? '')
  const [roleTitle, setRoleTitle] = useState(application?.roleTitle ?? '')
  const [jobPostingUrl, setJobPostingUrl] = useState(application?.jobPostingUrl ?? '')
  const [status, setStatus] = useState(application?.status ?? 'Applied')
  const [dateApplied, setDateApplied] = useState(application?.dateApplied ?? todayIso())
  const [notes, setNotes] = useState(application?.notes ?? '')
  const [jobDescriptionText, setJobDescriptionText] = useState(
    application?.jobDescriptionText ?? '',
  )
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      await onSave({
        companyName: companyName.trim(),
        roleTitle: roleTitle.trim(),
        jobPostingUrl: jobPostingUrl.trim() || null,
        dateApplied,
        notes: notes.trim() || null,
        jobDescriptionText: jobDescriptionText.trim() || null,
        ...(isEdit ? {} : { status }),
      })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Something went wrong.')
      setSaving(false)
    }
  }

  return (
    <div
      className="fixed inset-0 z-30 flex items-start justify-center overflow-y-auto bg-slate-900/40 p-4 pt-12"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <form
        onSubmit={submit}
        className="w-full max-w-xl rounded-2xl bg-white p-6 shadow-xl"
        role="dialog"
        aria-modal="true"
        aria-label={isEdit ? 'Edit application' : 'Add application'}
      >
        <div className="mb-5 flex items-start justify-between">
          <h2 className="text-lg font-semibold text-slate-900">
            {isEdit ? `Edit — ${application.companyName}` : 'Add application'}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded-md p-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600"
          >
            ✕
          </button>
        </div>

        <div className="grid gap-4 sm:grid-cols-2">
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-slate-700">Company *</span>
            <input
              className={inputClass}
              value={companyName}
              onChange={(e) => setCompanyName(e.target.value)}
              required
              maxLength={200}
              autoFocus
            />
          </label>
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-slate-700">Role title *</span>
            <input
              className={inputClass}
              value={roleTitle}
              onChange={(e) => setRoleTitle(e.target.value)}
              required
              maxLength={200}
            />
          </label>
          <label className="block text-sm sm:col-span-2">
            <span className="mb-1 block font-medium text-slate-700">Job posting URL</span>
            <input
              className={inputClass}
              type="url"
              placeholder="https://…"
              value={jobPostingUrl}
              onChange={(e) => setJobPostingUrl(e.target.value)}
              maxLength={2048}
            />
          </label>
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-slate-700">Date applied *</span>
            <input
              className={inputClass}
              type="date"
              value={dateApplied}
              onChange={(e) => setDateApplied(e.target.value)}
              required
            />
          </label>
          {!isEdit && (
            <label className="block text-sm">
              <span className="mb-1 block font-medium text-slate-700">Status</span>
              <select
                className={inputClass}
                value={status}
                onChange={(e) => setStatus(e.target.value as typeof status)}
              >
                {STATUSES.map((s) => (
                  <option key={s} value={s}>
                    {STATUS_META[s].label}
                  </option>
                ))}
              </select>
            </label>
          )}
          <label className="block text-sm sm:col-span-2">
            <span className="mb-1 block font-medium text-slate-700">Notes</span>
            <textarea
              className={inputClass}
              rows={3}
              placeholder="Recruiter names, referral, comp range, next steps…"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </label>
          <label className="block text-sm sm:col-span-2">
            <span className="mb-1 block font-medium text-slate-700">Job description</span>
            <textarea
              className={inputClass}
              rows={5}
              placeholder="Paste the full job description — used for match scoring later."
              value={jobDescriptionText}
              onChange={(e) => setJobDescriptionText(e.target.value)}
            />
          </label>
        </div>

        {error && (
          <p className="mt-4 rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>
        )}

        <div className="mt-6 flex justify-end gap-2">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg px-4 py-2 text-sm font-medium text-slate-600 hover:bg-slate-100"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={saving}
            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-60"
          >
            {saving ? 'Saving…' : isEdit ? 'Save changes' : 'Add application'}
          </button>
        </div>
      </form>
    </div>
  )
}
