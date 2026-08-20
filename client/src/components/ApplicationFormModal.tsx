import { useState, type FormEvent } from 'react'
import { api, ApiError } from '../api/client'
import { STATUSES, type Application, type ApplicationInput } from '../api/types'
import { STATUS_META } from '../lib/status'
import { fieldClass as inputClass } from '../lib/styles'
import { ModalBackdrop, ModalHeader } from './Modal'

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

  const [extractUrl, setExtractUrl] = useState('')
  const [extracting, setExtracting] = useState(false)
  const [extractError, setExtractError] = useState<string | null>(null)
  const [extracted, setExtracted] = useState(false)

  const extractFromUrl = async () => {
    const url = extractUrl.trim()
    if (!url) return
    setExtracting(true)
    setExtractError(null)
    try {
      const result = await api.extractJobPosting(url)
      setCompanyName(result.companyName)
      setRoleTitle(result.roleTitle)
      setJobDescriptionText(result.jobDescriptionText)
      setJobPostingUrl(url)
      setExtracted(true)
    } catch (e) {
      setExtractError(e instanceof ApiError ? e.message : 'Something went wrong.')
    } finally {
      setExtracting(false)
    }
  }

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
    <ModalBackdrop onClose={onClose}>
      <form
        onSubmit={submit}
        className="w-full max-w-2xl overflow-hidden rounded-2xl bg-white shadow-2xl"
        role="dialog"
        aria-modal="true"
        aria-label={isEdit ? 'Edit application' : 'Add application'}
      >
        <ModalHeader
          title={isEdit ? 'Edit application' : 'Add application'}
          subtitle={
            isEdit
              ? application.companyName
              : prefill
                ? 'Detected from Gmail — review the details, then paste the job description to unlock match scoring.'
                : 'Paste the job description to unlock match scoring.'
          }
          onClose={onClose}
        />

        {!isEdit && (
          <div className="border-b border-slate-100 bg-brand-50/50 px-7 py-5">
            <span className={labelClass}>
              ✨ Fill in from a job posting URL
            </span>
            <div className="flex gap-2">
              <input
                className={inputClass}
                type="url"
                placeholder="https://…"
                value={extractUrl}
                onChange={(e) => {
                  setExtractUrl(e.target.value)
                  setExtracted(false)
                }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') {
                    e.preventDefault()
                    void extractFromUrl()
                  }
                }}
              />
              <button
                type="button"
                onClick={() => void extractFromUrl()}
                disabled={extracting || !extractUrl.trim()}
                className="brand-gradient shrink-0 rounded-xl px-5 py-2.5 text-base font-semibold text-white shadow-lg shadow-brand-600/25 transition hover:opacity-95 disabled:opacity-60"
              >
                {extracting ? 'Reading…' : 'Fill in'}
              </button>
            </div>
            {extractError && <p className="mt-2 text-sm font-medium text-rose-700">{extractError}</p>}
            {extracted && !extractError && (
              <p className="mt-2 text-sm font-medium text-emerald-700">
                Filled in below — review before saving, especially the job description.
              </p>
            )}
          </div>
        )}

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
    </ModalBackdrop>
  )
}
