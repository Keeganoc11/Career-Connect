import { useCallback, useEffect, useMemo, useState, type FormEvent } from 'react'
import { ChevronRight, Download, Sparkles } from 'lucide-react'
import { api, ApiError } from '../api/client'
import type { Application, PrepRun, ResumeSummary } from '../api/types'
import { formatRelative, todayIso } from '../lib/format'
import { errorMessage } from '../lib/errors'
import { tailoringSummary } from '../lib/tailoring'
import { useAsyncAction } from '../lib/useAsyncAction'
import {
  Banner,
  Button,
  Card,
  Field,
  IconButton,
  Input,
  LoadingState,
  PageHeader,
  Spinner,
  Textarea,
  toast,
} from '../components/ui'

interface Props {
  dataVersion: number
  onOpenJob: (applicationId: string) => void
  onGoToResumes: () => void
  onDataChanged: () => void
}

const MIN_DESCRIPTION = 200
const RECENT_LIMIT = 8

interface Duplicate {
  applicationId: string
  message: string
}

/**
 * The front door: paste a job description, get a tailored resume. Everything
 * else about adding a job — company, role, date, status — is worked out from
 * the paste, so there's no form between finding a job and tailoring for it.
 */
export function TailorPage({ dataVersion, onOpenJob, onGoToResumes, onDataChanged }: Props) {
  const [text, setText] = useState('')
  const [url, setUrl] = useState('')
  const [duplicate, setDuplicate] = useState<Duplicate | null>(null)
  const capture = useAsyncAction()

  const [applications, setApplications] = useState<Application[]>([])
  const [prepRuns, setPrepRuns] = useState<Record<string, PrepRun>>({})
  const [resumes, setResumes] = useState<ResumeSummary[] | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    try {
      const [list, runs, resumeList] = await Promise.all([
        api.listApplications(),
        api.listPrepRuns(),
        api.listResumes(),
      ])
      setApplications(list)
      setPrepRuns(runs)
      setResumes(resumeList)
      setLoadError(null)
    } catch (e) {
      setLoadError(errorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh, dataVersion])

  const recent = useMemo(
    () =>
      applications
        .filter((a) => prepRuns[a.id])
        .sort((a, b) => prepRuns[b.id].startedAtUtc.localeCompare(prepRuns[a.id].startedAtUtc))
        .slice(0, RECENT_LIMIT),
    [applications, prepRuns],
  )

  // Passes finish server-side whether or not anyone is watching; poll so the
  // list moves on from "Scoring…" by itself.
  const anyRunning = recent.some((a) => prepRuns[a.id].status === 'Running')
  useEffect(() => {
    if (!anyRunning) return
    const timer = setInterval(() => void refresh(), 5000)
    return () => clearInterval(timer)
  }, [anyRunning, refresh])

  const activeResume = resumes?.find((r) => r.isActive)
  const resumeReady = activeResume?.hasLayout === true

  const trimmed = text.trim()
  const tooShort = trimmed.length > 0 && trimmed.length < MIN_DESCRIPTION
  const canSubmit = trimmed.length >= MIN_DESCRIPTION || (trimmed.length === 0 && url.trim().length > 0)

  const submit = (allowDuplicate = false) =>
    void capture.run(async () => {
      setDuplicate(null)
      try {
        const result = await api.captureJob({
          jobDescriptionText: trimmed || null,
          jobPostingUrl: url.trim() || null,
          localDate: todayIso(),
          allowDuplicate,
        })
        setText('')
        setUrl('')
        onDataChanged()
        if (result.prepMessage) toast.info(result.prepMessage)
        onOpenJob(result.application.id)
      } catch (e) {
        const existing = e instanceof ApiError && e.status === 409 ? e.problem?.existingApplicationId : null
        if (typeof existing === 'string') {
          setDuplicate({ applicationId: existing, message: e instanceof Error ? e.message : '' })
          return
        }
        throw e
      }
    })

  const onSubmit = (event: FormEvent) => {
    event.preventDefault()
    if (canSubmit) submit()
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title="Tailor a resume"
        description="Paste a job description. You get your resume rewritten for it, in your format, and an honest read on your chances."
      />

      {resumes !== null && !resumeReady && (
        <Banner
          tone="warning"
          action={
            <Button size="sm" onClick={onGoToResumes}>
              Go to Resumes
            </Button>
          }
        >
          {activeResume
            ? 'Your active resume isn’t a PDF, so its format can’t be kept. Upload your resume PDF and make it active.'
            : 'Upload your resume as a PDF first — every tailored version is built from it.'}
        </Banner>
      )}

      <Card>
        <form onSubmit={onSubmit} className="space-y-4">
          <Field
            label="Job description"
            error={
              tooShort
                ? 'That looks too short — paste the whole posting, responsibilities and requirements included.'
                : null
            }
            hint="Copy everything from the posting on LinkedIn, Indeed, or the company’s site. Extra page text is fine."
          >
            {(props) => (
              <Textarea
                {...props}
                rows={12}
                value={text}
                maxLength={60000}
                placeholder="Paste the full job description…"
                onChange={(e) => {
                  setText(e.target.value)
                  setDuplicate(null)
                }}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && (e.metaKey || e.ctrlKey) && canSubmit) {
                    e.preventDefault()
                    submit()
                  }
                }}
              />
            )}
          </Field>

          <Field
            label="Link to the posting (optional)"
            hint="Saved with the job. If you paste nothing above, it's read instead — company sites usually allow that, LinkedIn and Indeed don't."
          >
            {(props) => (
              <Input
                {...props}
                type="url"
                inputMode="url"
                placeholder="https://…"
                maxLength={2048}
                value={url}
                onChange={(e) => setUrl(e.target.value)}
              />
            )}
          </Field>

          {capture.error && <Banner>{capture.error}</Banner>}

          {duplicate && (
            <Banner
              tone="info"
              action={
                <div className="flex gap-2">
                  <Button size="sm" onClick={() => onOpenJob(duplicate.applicationId)}>
                    Open it
                  </Button>
                  <Button size="sm" variant="ghost" onClick={() => submit(true)}>
                    Add it again
                  </Button>
                </div>
              }
            >
              {duplicate.message}
            </Banner>
          )}

          <div className="flex flex-wrap items-center justify-between gap-3">
            <p className="text-xs text-fg-muted">Takes two or three minutes. You can leave this page.</p>
            <Button
              variant="primary"
              type="submit"
              icon={<Sparkles className="size-4" aria-hidden />}
              loading={capture.busy}
              disabled={!canSubmit}
            >
              {capture.busy ? 'Reading the posting…' : 'Tailor my resume'}
            </Button>
          </div>
        </form>
      </Card>

      <section>
        <h2 className="mb-2 text-base font-semibold text-fg">Recent</h2>
        {loading ? (
          <LoadingState />
        ) : loadError ? (
          <Banner>{loadError}</Banner>
        ) : recent.length === 0 ? (
          <p className="text-sm text-fg-muted">Jobs you tailor for show up here.</p>
        ) : (
          <Card padded={false}>
            <ul className="divide-y divide-line">
              {recent.map((application) => (
                <RecentRow
                  key={application.id}
                  application={application}
                  run={prepRuns[application.id]}
                  onOpen={() => onOpenJob(application.id)}
                />
              ))}
            </ul>
          </Card>
        )}
      </section>
    </div>
  )
}

function RecentRow({
  application,
  run,
  onOpen,
}: {
  application: Application
  run: PrepRun
  onOpen: () => void
}) {
  const download = useAsyncAction()
  const running = run.status === 'Running'

  useEffect(() => {
    if (download.error) toast.error(download.error)
  }, [download.error])

  return (
    <li className="flex items-center gap-2 pr-2">
      <button
        type="button"
        onClick={onOpen}
        className="flex min-w-0 flex-1 items-center gap-3 px-4 py-3 text-left transition-colors hover:bg-surface-muted"
      >
        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-medium text-fg">
            {application.companyName}{' '}
            <span className="font-normal text-fg-muted">· {application.roleTitle}</span>
          </span>
          <span className="mt-0.5 flex items-center gap-1.5 text-xs text-fg-muted">
            {running && <Spinner />}
            <span className="truncate">{tailoringSummary(run)}</span>
            <span aria-hidden>·</span>
            <span className="shrink-0">{formatRelative(run.startedAtUtc)}</span>
          </span>
        </span>
        <ChevronRight className="size-4 shrink-0 text-fg-subtle" aria-hidden />
      </button>
      {run.status === 'Succeeded' && run.review && (
        <IconButton
          label={`Download the ${application.companyName} resume`}
          icon={download.busy ? <Spinner /> : <Download className="size-4" aria-hidden />}
          onClick={() => void download.run(() => api.downloadTailoredResumePdf(application.id))}
        />
      )}
    </li>
  )
}
