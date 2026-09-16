import { useCallback, useEffect, useRef, useState } from 'react'
import {
  ArrowLeft,
  CalendarDays,
  Check,
  FileText,
  MessageSquareText,
  MoreHorizontal,
  Pencil,
  RotateCcw,
  Sparkles,
  Trash2,
} from 'lucide-react'
import { api, ApiError } from '../api/client'
import type { Application, ApplicationStatus, PrepRun, StatusChange } from '../api/types'
import { PrepProgress } from '../components/PrepProgress'
import { StatusMenu } from '../components/StatusMenu'
import { TailoringReview } from '../components/TailoringReview'
import {
  Banner,
  Button,
  Card,
  ConfirmDialog,
  EmptyState,
  Field,
  IconButton,
  LoadingState,
  Menu,
  Textarea,
  TextLink,
  toast,
} from '../components/ui'
import { errorMessage } from '../lib/errors'
import { formatDate, formatDateTime, formatUntil } from '../lib/format'
import { KIND_LABELS } from '../lib/interviews'
import { STATUS_LABELS } from '../lib/status'
import type { TrackerIntentRequest } from '../lib/trackerIntent'
import { useAsyncAction } from '../lib/useAsyncAction'

interface Props {
  applicationId: string
  dataVersion: number
  onBack: () => void
  /** Opens one of the tracker's dialogs — edit, interviews, cover letter — over this page. */
  onIntent: (request: TrackerIntentRequest) => void
  onDataChanged: () => void
}

const POLL_INTERVAL_MS = 2500

const SOURCE_LABELS: Record<string, string> = {
  Manual: 'You',
  EmailSuggestion: 'From an email you accepted',
  EmailAutomatic: 'From an email',
}

/**
 * Everything about one job in one place. It replaces the prep, match score and
 * cover letter dialogs that used to be scattered behind a row's "…" menu: the
 * tailored resume and its reality check are the page, and the rest hangs off it.
 */
export function JobPage({ applicationId, dataVersion, onBack, onIntent, onDataChanged }: Props) {
  const [application, setApplication] = useState<Application | null>(null)
  const [run, setRun] = useState<PrepRun | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [missing, setMissing] = useState(false)
  const [request, setRequest] = useState('')
  const [confirmStartOver, setConfirmStartOver] = useState(false)

  const start = useAsyncAction()
  const applied = useAsyncAction()

  const load = useCallback(async () => {
    try {
      const [loaded, latest] = await Promise.all([
        api.getApplication(applicationId),
        api.getPrepRun(applicationId).catch((e: unknown) => {
          if (e instanceof ApiError && e.status === 404) return null
          throw e
        }),
      ])
      setApplication(loaded)
      setRun(latest)
      setMissing(false)
      setLoadError(null)
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) setMissing(true)
      else setLoadError(errorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [applicationId])

  useEffect(() => {
    void load()
  }, [load, dataVersion])

  useEffect(() => {
    if (application) document.title = `${application.companyName} · Career Connect`
  }, [application])

  // A pass is several chained model calls with no response to wait on — the
  // run row is the progress, so poll it until it settles.
  const running = run?.status === 'Running'
  useEffect(() => {
    if (!running) return
    let cancelled = false
    const timer = setInterval(async () => {
      try {
        const latest = await api.getPrepRun(applicationId)
        if (cancelled) return
        setRun(latest)
        if (latest.status !== 'Running') {
          onDataChanged()
          if (latest.status === 'Succeeded') toast.success('Your tailored resume is ready.')
        }
      } catch {
        // A blip between polls — the next tick retries.
      }
    }, POLL_INTERVAL_MS)
    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [running, applicationId, onDataChanged])

  const startTailoring = (instructions?: string) =>
    start.run(async () => {
      setRun(await api.startPrep(applicationId, instructions))
      setRequest('')
      window.scrollTo({ top: 0, behavior: 'smooth' })
    })

  const changeStatus = async (status: ApplicationStatus) => {
    try {
      setApplication(await api.updateStatus(applicationId, status))
      onDataChanged()
      toast.success(`Marked as ${STATUS_LABELS[status].toLowerCase()}.`)
    } catch (e) {
      toast.error(errorMessage(e) ?? 'Could not update the status.')
    }
  }

  const markApplied = () =>
    void applied.run(async () => {
      setApplication(await api.updateStatus(applicationId, 'Applied'))
      onDataChanged()
      toast.success('Marked as applied. Good luck.')
    })

  if (loading) return <LoadingState />

  if (missing || !application) {
    return (
      <div className="space-y-4">
        <BackButton onBack={onBack} />
        {loadError ? (
          <Banner action={<Button size="sm" onClick={() => void load()}>Try again</Button>}>
            {loadError}
          </Banner>
        ) : (
          <EmptyState title="This job no longer exists" description="It may have been deleted." />
        )}
      </div>
    )
  }

  const reviewed = run?.status === 'Succeeded' && run.review !== null

  return (
    <>
      <div className="space-y-6">
        <BackButton onBack={onBack} />

        <header className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h1 className="text-xl font-semibold text-fg">{application.companyName}</h1>
            <p className="text-sm text-fg-muted">{application.roleTitle}</p>
            <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-2 text-sm text-fg-muted">
              <StatusMenu value={application.status} onChange={(status) => void changeStatus(status)} />
              <span className="tabular-nums">
                {application.status === 'Preparing' ? 'Added' : 'Applied'} {formatDate(application.dateApplied)}
              </span>
              {application.jobPostingUrl && (
                <TextLink href={application.jobPostingUrl} external>
                  Job posting
                </TextLink>
              )}
            </div>
          </div>

          <div className="flex items-center gap-2">
            {application.status === 'Preparing' && (
              <Button
                variant={reviewed ? 'primary' : 'secondary'}
                icon={<Check className="size-4" aria-hidden />}
                loading={applied.busy}
                onClick={markApplied}
              >
                I applied
              </Button>
            )}
            <JobActions application={application} onIntent={onIntent} />
          </div>
        </header>

        {applied.error && <Banner>{applied.error}</Banner>}
        {loadError && <Banner>{loadError}</Banner>}

        {/* The resume and its reality check are the page. */}
        {!run ? (
          <Card>
            <EmptyState
              icon={<Sparkles className="size-5" aria-hidden />}
              title="Not tailored yet"
              description={
                application.jobDescriptionText
                  ? 'Rewrites your resume for this posting in your own format, then tells you honestly how you stack up.'
                  : 'Add the job description to tailor your resume for this job.'
              }
              action={
                application.jobDescriptionText ? (
                  <Button
                    variant="primary"
                    icon={<Sparkles className="size-4" aria-hidden />}
                    loading={start.busy}
                    onClick={() => void startTailoring()}
                  >
                    Tailor my resume
                  </Button>
                ) : (
                  <Button onClick={() => onIntent({ kind: 'edit', applicationId })}>
                    Add job description
                  </Button>
                )
              }
            />
            {start.error && <Banner>{start.error}</Banner>}
          </Card>
        ) : run.status === 'Running' ? (
          <Card>
            <h2 className="text-base font-semibold text-fg">Tailoring your resume…</h2>
            <p className="mt-0.5 mb-4 text-sm text-fg-muted">
              {run.instructions
                ? `Working on your request: “${run.instructions}”`
                : 'Usually two or three minutes. It keeps going if you leave this page.'}
            </p>
            <PrepProgress run={run} />
          </Card>
        ) : run.status === 'Failed' ? (
          <div className="space-y-3">
            <Banner
              action={
                <Button size="sm" loading={start.busy} onClick={() => void startTailoring()}>
                  Try again
                </Button>
              }
            >
              {run.errorMessage ?? 'Tailoring didn’t finish.'}
            </Banner>
            {start.error && <Banner>{start.error}</Banner>}
            {run.steps.length > 0 && <StepsDisclosure run={run} />}
          </div>
        ) : reviewed ? (
          <div className="space-y-5">
            <TailoringReview
              applicationId={applicationId}
              run={run}
              afterVerdict={
                <Card>
                  <form
                    onSubmit={(event) => {
                      event.preventDefault()
                      if (request.trim()) void startTailoring(request)
                    }}
                    className="space-y-3"
                  >
                    <Field
                      label="Ask for changes"
                      hint="Builds on this version. It still won’t claim anything your resume and extra facts don’t back up."
                    >
                      {(props) => (
                        <Textarea
                          {...props}
                          rows={2}
                          maxLength={1000}
                          value={request}
                          onChange={(e) => setRequest(e.target.value)}
                          placeholder="e.g. Lean more backend. Mention SQL Server before React."
                        />
                      )}
                    </Field>
                    {start.error && <Banner>{start.error}</Banner>}
                    <div className="flex flex-wrap justify-between gap-2">
                      <Button
                        variant="ghost"
                        icon={<RotateCcw className="size-4" aria-hidden />}
                        onClick={() => setConfirmStartOver(true)}
                      >
                        Start over
                      </Button>
                      <Button type="submit" loading={start.busy} disabled={!request.trim()}>
                        Rewrite
                      </Button>
                    </div>
                  </form>
                </Card>
              }
            />
            <StepsDisclosure run={run} />
          </div>
        ) : (
          <Card>
            <EmptyState
              title="Tailored before resumes kept their format"
              description="Run it again to get a PDF in your resume's exact format and a reality check."
              action={
                <Button variant="primary" loading={start.busy} onClick={() => void startTailoring()}>
                  Tailor again
                </Button>
              }
            />
            {start.error && <Banner>{start.error}</Banner>}
          </Card>
        )}

        {application.interviews.length > 0 && (
          <section>
            <div className="mb-2 flex items-center justify-between gap-2">
              <h2 className="text-base font-semibold text-fg">Interviews</h2>
              <Button size="sm" onClick={() => onIntent({ kind: 'interviews', applicationId })}>
                Manage
              </Button>
            </div>
            <Card padded={false}>
              <ul className="divide-y divide-line">
                {[...application.interviews]
                  .sort((a, b) => a.scheduledAtUtc.localeCompare(b.scheduledAtUtc))
                  .map((interview) => (
                    <li key={interview.id} className="flex flex-wrap justify-between gap-2 px-4 py-3 text-sm">
                      <span className="text-fg">
                        {KIND_LABELS[interview.kind]} · {formatDateTime(interview.scheduledAtUtc)}
                      </span>
                      <span className="text-fg-muted">{formatUntil(interview.scheduledAtUtc)}</span>
                    </li>
                  ))}
              </ul>
            </Card>
          </section>
        )}

        {application.jobDescriptionText && (
          <details className="group rounded-surface border border-line bg-surface">
            <summary className="cursor-pointer list-none px-4 py-3 text-sm font-semibold text-fg">
              Job description
              <span className="ml-2 text-xs font-normal text-fg-muted group-open:hidden">Show</span>
            </summary>
            <p className="border-t border-line px-4 py-3 text-sm leading-relaxed whitespace-pre-wrap text-fg-muted">
              {application.jobDescriptionText}
            </p>
          </details>
        )}

        {application.statusHistory && application.statusHistory.length > 0 && (
          <Timeline history={application.statusHistory} />
        )}
      </div>

      {confirmStartOver && (
        <ConfirmDialog
          title="Start over?"
          body="Tailors again from your base resume. This version, and any changes you asked for, are replaced."
          confirmLabel="Start over"
          busyLabel="Starting…"
          busy={start.busy}
          error={start.error}
          onCancel={() => setConfirmStartOver(false)}
          onConfirm={() => {
            void startTailoring().then((ok) => {
              if (ok) setConfirmStartOver(false)
            })
          }}
        />
      )}
    </>
  )
}

function BackButton({ onBack }: { onBack: () => void }) {
  return (
    <Button variant="ghost" size="sm" icon={<ArrowLeft className="size-4" aria-hidden />} onClick={onBack}>
      Back
    </Button>
  )
}

function StepsDisclosure({ run }: { run: PrepRun }) {
  return (
    <details className="group">
      <summary className="cursor-pointer list-none text-sm font-medium text-fg-muted hover:text-fg">
        <span className="group-open:hidden">Show how it got here</span>
        <span className="hidden group-open:inline">Hide steps</span>
      </summary>
      <div className="mt-3">
        <PrepProgress run={run} />
      </div>
    </details>
  )
}

function Timeline({ history }: { history: StatusChange[] }) {
  const ordered = [...history].sort((a, b) => b.changedAtUtc.localeCompare(a.changedAtUtc))

  return (
    <section>
      <h2 className="mb-2 text-base font-semibold text-fg">Timeline</h2>
      <ol className="space-y-2 border-l border-line pl-4">
        {ordered.map((change, index) => (
          <li key={index} className="text-sm">
            <p className="text-fg">
              {change.fromStatus
                ? `${STATUS_LABELS[change.fromStatus]} → ${STATUS_LABELS[change.toStatus]}`
                : `Added as ${STATUS_LABELS[change.toStatus].toLowerCase()}`}
            </p>
            <p className="text-xs text-fg-muted">
              {formatDateTime(change.changedAtUtc)} · {SOURCE_LABELS[change.source] ?? change.source}
            </p>
          </li>
        ))}
      </ol>
    </section>
  )
}

function JobActions({
  application,
  onIntent,
}: {
  application: Application
  onIntent: (request: TrackerIntentRequest) => void
}) {
  const buttonRef = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)
  const applicationId = application.id

  return (
    <>
      <IconButton
        ref={buttonRef}
        label={`More actions for ${application.companyName}`}
        icon={<MoreHorizontal className="size-4" aria-hidden />}
        onClick={() => setOpen((v) => !v)}
      />
      {open && (
        <Menu
          anchorRef={buttonRef}
          label={`More actions for ${application.companyName}`}
          onClose={() => setOpen(false)}
          items={[
            {
              key: 'edit',
              label: 'Edit details',
              icon: <Pencil className="size-4" aria-hidden />,
              onSelect: () => onIntent({ kind: 'edit', applicationId }),
            },
            {
              key: 'interviews',
              label: 'Interviews',
              icon: <CalendarDays className="size-4" aria-hidden />,
              onSelect: () => onIntent({ kind: 'interviews', applicationId }),
            },
            {
              key: 'cover-letter',
              label: 'Cover letter',
              icon: <FileText className="size-4" aria-hidden />,
              onSelect: () => onIntent({ kind: 'coverLetter', applicationId }),
            },
            {
              key: 'interview-prep',
              label: 'Interview prep',
              icon: <MessageSquareText className="size-4" aria-hidden />,
              onSelect: () => onIntent({ kind: 'interviewPrep', applicationId }),
            },
            {
              key: 'delete',
              label: 'Delete job',
              icon: <Trash2 className="size-4" aria-hidden />,
              destructive: true,
              onSelect: () => onIntent({ kind: 'delete', applicationId }),
            },
          ]}
        />
      )}
    </>
  )
}
