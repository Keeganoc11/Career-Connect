import { useState, type FormEvent } from 'react'
import { STATUSES, type Application, type ApplicationInput } from '../api/types'
import { STATUS_META } from '../lib/status'
import { useEscapeKey } from '../lib/useEscapeKey'

interface Props {
  /** null = creating a new application. */
  application: Application | null
  /** Initial field values when creating a new application (e.g. from a Gmail suggestion). Ignored when editing. */
  prefill?: Partial<Pick<ApplicationInput, 'companyName' | 'roleTitle' | 'dateApplied'>>
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
  'w-full rounded-xl border border-slate-300 px-4 py-2.5 text-base text-slate-900 placeholder:text-slate-400 focus:border-brand-500 focus:outline-none focus:ring-4 focus:ring-brand-500/15'

const labelClass = 'mb-1.5 block text-sm font-semibold text-slate-700'

export function ApplicationFormModal({ application, prefill, onSave, onClose }: Props) {
  const isEdit = application !== null
  const [companyName, setCompanyName] = useState(application?.companyName ?? prefill?.companyName ?? '')
  const [roleTitle, setRoleTitle] = useState(application?.roleTitle ?? prefill?.roleTitle ?? '')
  const [jobPostingUrl, setJobPostingUrl] = useState(application?.jobPostingUrl ?? '')
  const [status, setStatus] = useState(application?.status ?? 'Applied')
  const [dateApplied, setDateApplied] = useState(application?.dateApplied ?? prefill?.dateApplied ?? todayIso())
  const [notes, setNotes] = useState(application?.notes ?? '')
  const [jobDescriptionText, setJobDescriptionText] = useState(
    application?.jobDescriptionText ?? '',
  )
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEscapeKey(onClose)

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
      className="fixed inset-0 z-30 flex items-start justify-center overflow-y-auto bg-slate-900/50 p-4 pt-10 backdrop-blur-sm"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <form
        onSubmit={submit}
        className="w-full max-w-2xl overflow-hidden rounded-2xl bg-white shadow-2xl"
        role="dialog"
        aria-modal="true"
        aria-label={isEdit ? 'Edit application' : 'Add application'}
      >
        <div className="brand-gradient flex items-start justify-between px-7 py-5">
          <div>
            <h2 className="text-xl font-bold text-white">
              {isEdit ? 'Edit application' : 'Add application'}
            </h2>
            <p className="mt-0.5 text-sm text-white/80">
              {isEdit
                ? application.companyName
                : prefill
                  ? 'Detected from Gmail — review the details, then paste the job description to unlock match scoring.'
                  : 'Paste the job description to unlock match scoring.'}
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

        <div className="grid gap-5 p-7 sm:grid-cols-2">
          <label className="block">
            <span className={labelClass}>Company *</span>
            <input
              className={inputClass}
              value={companyName}
              onChange={(e) => setCompanyName(e.target.value)}
              required
              maxLength={200}
              autoFocus
            />
          </label>
          <label className="block">
            <span className={labelClass}>Role title *</span>
            <input
              className={inputClass}
              value={roleTitle}
              onChange={(e) => setRoleTitle(e.target.value)}
              required
              maxLength={200}
            />
          </label>
          <label className="block sm:col-span-2">
            <span className={labelClass}>Job posting URL</span>
            <input
              className={inputClass}
              type="url"
              placeholder="https://…"
              value={jobPostingUrl}
              onChange={(e) => setJobPostingUrl(e.target.value)}
              maxLength={2048}
            />
          </label>
          <label className="block">
            <span className={labelClass}>Date applied *</span>
            <input
              className={inputClass}
              type="date"
              value={dateApplied}
              onChange={(e) => setDateApplied(e.target.value)}
              required
            />
          </label>
          {!isEdit && (
            <label className="block">
              <span className={labelClass}>Status</span>
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
          <label className="block sm:col-span-2">
            <span className={labelClass}>Notes</span>
            <textarea
              className={inputClass}
              rows={3}
              placeholder="Recruiter names, referral, comp range, next steps…"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
            />
          </label>
          <label className="block sm:col-span-2">
            <span className={labelClass}>
              Job description
              <span className="ml-2 rounded-full bg-brand-50 px-2 py-0.5 text-xs font-semibold text-brand-700">
                powers match scoring
              </span>
            </span>
            <textarea
              className={`${inputClass} font-mono text-sm`}
              rows={6}
              placeholder="Paste the full job description here."
              value={jobDescriptionText}
              onChange={(e) => setJobDescriptionText(e.target.value)}
            />
          </label>

          {error && (
            <p className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-base font-medium text-rose-700 sm:col-span-2">
              {error}
            </p>
          )}
        </div>

        <div className="flex justify-end gap-2 border-t border-slate-100 bg-slate-50 px-7 py-5">
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl px-5 py-2.5 text-base font-semibold text-slate-600 transition hover:bg-slate-200/60"
          >
            Cancel
          </button>
          <button
            type="submit"
            disabled={saving}
            className="brand-gradient rounded-xl px-6 py-2.5 text-base font-semibold text-white shadow-lg shadow-brand-600/25 transition hover:opacity-95 disabled:opacity-60"
          >
            {saving ? 'Saving…' : isEdit ? 'Save changes' : 'Add application'}
          </button>
        </div>
      </form>
    </div>
  )
}
