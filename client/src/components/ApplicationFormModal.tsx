import { useId, useRef, useState, type FormEvent } from 'react'
import { Sparkles } from 'lucide-react'
import { api } from '../api/client'
import { STATUSES, type Application, type ApplicationInput } from '../api/types'
import { STATUS_LABELS } from '../lib/status'
import { useAsyncAction } from '../lib/useAsyncAction'
import { Banner, Button, Field, Input, Modal, Select, Textarea, toast } from './ui'

interface Props {
  /** null = creating a new application. */
  application: Application | null
  /** Initial values when creating (e.g. from an email update). Ignored when editing. */
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

export function ApplicationFormModal({ application, prefill, onSave, onClose }: Props) {
  const isEdit = application !== null
  const formId = useId()

  const [companyName, setCompanyName] = useState(
    application?.companyName ?? prefill?.companyName ?? '',
  )
  const [roleTitle, setRoleTitle] = useState(application?.roleTitle ?? prefill?.roleTitle ?? '')
  const [jobPostingUrl, setJobPostingUrl] = useState(application?.jobPostingUrl ?? '')
  const [status, setStatus] = useState(application?.status ?? 'Preparing')
  const [dateApplied, setDateApplied] = useState(
    application?.dateApplied ?? prefill?.dateApplied ?? todayIso(),
  )
  const [notes, setNotes] = useState(application?.notes ?? '')
  const [jobDescriptionText, setJobDescriptionText] = useState(application?.jobDescriptionText ?? '')
  const [filledIn, setFilledIn] = useState(false)

  const save = useAsyncAction()
  const fill = useAsyncAction()

  // Snapshotted once, so the dirty guard compares against what was opened
  // rather than against whatever the last keystroke was.
  const initial = useRef({
    companyName,
    roleTitle,
    jobPostingUrl,
    status,
    dateApplied,
    notes,
    jobDescriptionText,
  })

  const dirty =
    companyName !== initial.current.companyName ||
    roleTitle !== initial.current.roleTitle ||
    jobPostingUrl !== initial.current.jobPostingUrl ||
    status !== initial.current.status ||
    dateApplied !== initial.current.dateApplied ||
    notes !== initial.current.notes ||
    jobDescriptionText !== initial.current.jobDescriptionText

  /**
   * One URL field does both jobs now. It used to be two — a separate "fill in
   * from a posting URL" box above the form and a "Job posting URL" field
   * inside it — which meant typing the same link twice.
   */
  const fillFromUrl = () =>
    void fill.run(async () => {
      const url = jobPostingUrl.trim()
      if (!url) return
      const result = await api.extractJobPosting(url)
      setCompanyName(result.companyName)
      setRoleTitle(result.roleTitle)
      setJobDescriptionText(result.jobDescriptionText)
      setFilledIn(true)
    })

  const submit = (event: FormEvent) => {
    event.preventDefault()
    void save.run(async () => {
      await onSave({
        companyName: companyName.trim(),
        roleTitle: roleTitle.trim(),
        jobPostingUrl: jobPostingUrl.trim() || null,
        dateApplied,
        notes: notes.trim() || null,
        jobDescriptionText: jobDescriptionText.trim() || null,
        ...(isEdit ? {} : { status }),
      })
      toast.success(isEdit ? 'Changes saved.' : 'Application added.')
    })
  }

  return (
    <Modal
      title={isEdit ? 'Edit application' : 'Add application'}
      description={isEdit ? `${application.companyName} · ${application.roleTitle}` : undefined}
      error={save.error}
      busy={save.busy}
      dirty={dirty}
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose} disabled={save.busy}>
            Cancel
          </Button>
          <Button variant="primary" type="submit" form={formId} loading={save.busy}>
            {isEdit ? 'Save changes' : 'Add application'}
          </Button>
        </>
      }
    >
      <form id={formId} onSubmit={submit} className="grid gap-4 sm:grid-cols-2">
        <div className="sm:col-span-2">
          <Field
            label="Job posting URL"
            error={fill.error}
            hint={
              filledIn && !fill.error
                ? 'Filled in below — check the details, especially the job description.'
                : 'Paste a link and fill the rest in from it.'
            }
          >
            {(props) => (
              <div className="flex gap-2">
                <Input
                  {...props}
                  type="url"
                  placeholder="https://…"
                  value={jobPostingUrl}
                  maxLength={2048}
                  onChange={(e) => {
                    setJobPostingUrl(e.target.value)
                    setFilledIn(false)
                  }}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      e.preventDefault()
                      fillFromUrl()
                    }
                  }}
                />
                <Button
                  icon={<Sparkles className="size-4" aria-hidden />}
                  loading={fill.busy}
                  disabled={!jobPostingUrl.trim()}
                  onClick={fillFromUrl}
                >
                  Fill in
                </Button>
              </div>
            )}
          </Field>
        </div>

        <Field label="Company">
          {(props) => (
            <Input
              {...props}
              value={companyName}
              onChange={(e) => setCompanyName(e.target.value)}
              required
              maxLength={200}
              autoFocus
            />
          )}
        </Field>

        <Field label="Role">
          {(props) => (
            <Input
              {...props}
              value={roleTitle}
              onChange={(e) => setRoleTitle(e.target.value)}
              required
              maxLength={200}
            />
          )}
        </Field>

        <Field label={status === 'Preparing' && !isEdit ? 'Target date' : 'Date applied'}>
          {(props) => (
            <Input
              {...props}
              type="date"
              value={dateApplied}
              onChange={(e) => setDateApplied(e.target.value)}
              required
            />
          )}
        </Field>

        {!isEdit && (
          <Field label="Status">
            {(props) => (
              <Select
                {...props}
                value={status}
                onChange={(e) => setStatus(e.target.value as typeof status)}
              >
                {STATUSES.map((s) => (
                  <option key={s} value={s}>
                    {STATUS_LABELS[s]}
                  </option>
                ))}
              </Select>
            )}
          </Field>
        )}

        <div className="sm:col-span-2">
          <Field label="Notes">
            {(props) => (
              <Textarea
                {...props}
                rows={3}
                placeholder="Recruiter names, referral, comp range, next steps…"
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
              />
            )}
          </Field>
        </div>

        <div className="sm:col-span-2">
          <Field
            label="Job description"
            hint="Powers the match score, resume tailoring and cover letters."
          >
            {(props) => (
              <Textarea
                {...props}
                monospace
                rows={6}
                placeholder="Paste the full job description here."
                value={jobDescriptionText}
                onChange={(e) => setJobDescriptionText(e.target.value)}
              />
            )}
          </Field>
        </div>

        {prefill && !isEdit && (
          <div className="sm:col-span-2">
            <Banner tone="info">
              Detected from your email — check the details, then paste the job description to unlock
              prep.
            </Banner>
          </div>
        )}
      </form>
    </Modal>
  )
}
