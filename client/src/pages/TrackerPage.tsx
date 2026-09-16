import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import type {
  Application,
  ApplicationInput,
  ApplicationStatus,
  GmailConnectionStatus,
  MatchResult,
  PrepRun,
  ResumeSummary,
  TailorResumeInput,
} from '../api/types'
import { ApplicationsTable } from '../components/ApplicationsTable'
import { ApplicationsList } from '../components/ApplicationsList'
import { StatusFilter } from '../components/StatusFilter'
import { ApplicationFormModal } from '../components/ApplicationFormModal'
import { MatchDetailModal } from '../components/MatchDetailModal'
import { PrepModal } from '../components/PrepModal'
import { CoverLetterModal } from '../components/CoverLetterModal'
import { InterviewPrepModal } from '../components/InterviewPrepModal'
import { InterviewsModal } from '../components/InterviewsModal'
import {
  Banner,
  Button,
  ConfirmDialog,
  EmptyState,
  Field,
  Input,
  LoadingState,
  PageHeader,
  toast,
} from '../components/ui'
import { errorMessage } from '../lib/errors'
import { useAsyncAction } from '../lib/useAsyncAction'
import { sortApplications, type SortKey } from '../lib/sortApplications'
import type { TrackerIntent } from '../lib/trackerIntent'

interface Props {
  /** Bumped when something outside this page changed an application. */
  dataVersion: number
  /** A request from elsewhere to open one of this page's dialogs. */
  intent: TrackerIntent | null
  onIntentHandled: () => void
  /** A prefilled application was actually created, so its suggestion is spent. */
  onPrefilledSave: () => void
  /** Drives the calendar-sync hint when scheduling an interview. */
  gmailStatus: GmailConnectionStatus | null
}

export function TrackerPage({
  dataVersion,
  intent,
  onIntentHandled,
  onPrefilledSave,
  gmailStatus,
}: Props) {
  const [applications, setApplications] = useState<Application[]>([])
  const [matches, setMatches] = useState<Record<string, MatchResult>>({})
  const [prepRuns, setPrepRuns] = useState<Record<string, PrepRun>>({})
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [statusFilter, setStatusFilter] = useState<ApplicationStatus | null>(null)
  const [search, setSearch] = useState('')
  const [sortKey, setSortKey] = useState<SortKey>('dateApplied')
  const [sortAsc, setSortAsc] = useState(false)

  const [busyId, setBusyId] = useState<string | null>(null)
  const [scoringId, setScoringId] = useState<string | null>(null)
  const [matchTarget, setMatchTarget] = useState<Application | null>(null)
  const [prepTarget, setPrepTarget] = useState<{ application: Application; autoStart: boolean } | null>(null)
  const [coverLetterTarget, setCoverLetterTarget] = useState<Application | null>(null)
  const [interviewPrepTarget, setInterviewPrepTarget] = useState<Application | null>(null)
  const [interviewsTarget, setInterviewsTarget] = useState<Application | null>(null)
  const [resumes, setResumes] = useState<ResumeSummary[]>([])
  const [tailoring, setTailoring] = useState(false)
  const [formTarget, setFormTarget] = useState<Application | null | 'new'>(null)
  const [formPrefill, setFormPrefill] = useState<
    Partial<Pick<ApplicationInput, 'companyName' | 'roleTitle' | 'dateApplied'>> | undefined
  >(undefined)
  /** The open form was seeded from an email suggestion. */
  const [fromPrefill, setFromPrefill] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<Application | null>(null)
  const deletion = useAsyncAction()

  // Signing out on a 401 is handled once, inside api/client; errorMessage
  // returns null for it, so nothing renders on the way out.
  const handleError = useCallback((e: unknown) => setLoadError(errorMessage(e)), [])

  const refresh = useCallback(async () => {
    try {
      // No getSummary: the status counts are computed from this list, so they
      // can't disagree with the rows, and there's no second request popping the
      // layout after the page has drawn.
      const [list, latestMatches, latestPrepRuns, resumeList] = await Promise.all([
        api.listApplications(),
        api.listMatches(),
        api.listPrepRuns(),
        api.listResumes(),
      ])
      setApplications(list)
      setMatches(latestMatches)
      setPrepRuns(latestPrepRuns)
      setResumes(resumeList)
      setLoadError(null)
    } catch (e) {
      handleError(e)
    } finally {
      setLoading(false)
    }
  }, [handleError])

  useEffect(() => {
    void refresh()
  }, [refresh, dataVersion])

  /**
   * Something elsewhere asked for a dialog — the header's Email updates, or the
   * agenda. Resolved against the loaded list, so an intent arriving before the
   * data does waits for it rather than being dropped.
   */
  useEffect(() => {
    if (!intent) return

    if (intent.kind === 'new') {
      setFormPrefill(intent.prefill)
      setFromPrefill(Boolean(intent.prefill))
      setFormTarget('new')
      onIntentHandled()
      return
    }

    const application = applications.find((a) => a.id === intent.applicationId)
    if (!application) return

    switch (intent.kind) {
      case 'edit':
        setFormTarget(application)
        break
      case 'prep':
        setPrepTarget({ application, autoStart: false })
        break
      case 'interviews':
        setInterviewsTarget(application)
        break
      case 'interviewPrep':
        setInterviewPrepTarget(application)
        break
      case 'coverLetter':
        setCoverLetterTarget(application)
        break
      case 'open':
        if (matches[application.id]) setMatchTarget(application)
        else setFormTarget(application)
        break
    }
    onIntentHandled()
  }, [intent, applications, matches, onIntentHandled])

  // A prep pass keeps running server-side after its dialog is closed, so the
  // list polls for itself — otherwise a row would sit on "Prepping…" until the
  // next full page load.
  const hasRunningPrep = Object.values(prepRuns).some((run) => run.status === 'Running')
  useEffect(() => {
    if (!hasRunningPrep) return

    let cancelled = false
    const timer = setInterval(async () => {
      try {
        const latest = await api.listPrepRuns()
        if (cancelled) return
        setPrepRuns(latest)

        // A finished pass wrote documents onto the applications themselves.
        if (!Object.values(latest).some((run) => run.status === 'Running')) {
          await refresh()
        }
      } catch {
        // Transient — the next tick retries, and a real outage surfaces elsewhere.
      }
    }, 5000)

    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [hasRunningPrep, refresh])

  const visible = useMemo(() => {
    const query = search.trim().toLowerCase()
    const filtered = applications.filter(
      (a) =>
        (statusFilter === null || a.status === statusFilter) &&
        (query === '' ||
          a.companyName.toLowerCase().includes(query) ||
          a.roleTitle.toLowerCase().includes(query)),
    )
    // Sorted once here, so the table and the phone list are never in different
    // orders.
    return sortApplications(filtered, matches, sortKey, sortAsc)
  }, [applications, statusFilter, search, matches, sortKey, sortAsc])

  const filtered = statusFilter !== null || search.trim() !== ''

  const clearFilters = () => {
    setStatusFilter(null)
    setSearch('')
  }

  const sort = (key: SortKey) => {
    if (key === sortKey) {
      setSortAsc((v) => !v)
    } else {
      setSortKey(key)
      setSortAsc(key === 'companyName' || key === 'status')
    }
  }

  const changeStatus = async (id: string, status: ApplicationStatus): Promise<boolean> => {
    setBusyId(id)
    try {
      await api.updateStatus(id, status)
      await refresh()
      return true
    } catch (e) {
      toast.error(errorMessage(e) ?? 'Could not update that status.')
      return false
    } finally {
      setBusyId(null)
    }
  }

  const score = async (application: Application) => {
    setScoringId(application.id)
    try {
      const result = await api.scoreMatch(application.id)
      setMatches((current) => ({ ...current, [application.id]: result }))
      // Open the detail straight away — the score alone rarely answers
      // "so what should I change?", which is the point of running it.
      setMatchTarget(application)
    } catch (e) {
      // A toast, not a standing amber banner above the table: scoring one row
      // failing isn't a state the whole page needs to sit in.
      toast.error(errorMessage(e) ?? 'Scoring failed.')
    } finally {
      setScoringId(null)
    }
  }

  const loadResumeContent = (id: string) => api.getResume(id)

  const tailorAndRescore = async (input: TailorResumeInput) => {
    if (!matchTarget) return
    setTailoring(true)
    try {
      const saved =
        input.mode === 'existing'
          ? await api.updateResume(input.resumeId, { label: input.label, content: input.content })
          : await api.createResume({ label: input.label, content: input.content })

      if (!saved.isActive) {
        await api.setActiveResume(saved.id)
      }

      const [result, resumeList] = await Promise.all([
        api.scoreMatch(matchTarget.id),
        api.listResumes(),
      ])
      setMatches((current) => ({ ...current, [matchTarget.id]: result }))
      setResumes(resumeList)
    } finally {
      setTailoring(false)
    }
  }

  const closeForm = () => {
    setFormTarget(null)
    setFormPrefill(undefined)
    setFromPrefill(false)
  }

  const save = async (input: ApplicationInput) => {
    let created: Application | null = null
    if (formTarget === 'new') {
      created = await api.createApplication(input)
    } else if (formTarget) {
      await api.updateApplication(formTarget.id, input)
    }

    if (created && fromPrefill) {
      onPrefilledSave()
    }
    closeForm()

    // The whole point of capturing a posting is to prep against it, so a new
    // one with a description goes straight into the pipeline rather than
    // waiting to be found and clicked in the list.
    if (created && created.jobDescriptionText && created.status === 'Preparing') {
      setPrepTarget({ application: created, autoStart: true })
    }

    await refresh()
  }

  const recordPrepRun = useCallback((run: PrepRun) => {
    setPrepRuns((current) => ({ ...current, [run.applicationId]: run }))
  }, [])

  const markApplied = async () => {
    if (!prepTarget) return
    const succeeded = await changeStatus(prepTarget.application.id, 'Applied')
    if (succeeded) {
      setPrepTarget(null)
      toast.success('Marked as applied.')
    }
  }

  // The dialog stays open and shows the reason if this fails, rather than
  // closing as though the delete worked.
  const confirmDelete = () =>
    void deletion.run(async () => {
      if (!deleteTarget) return
      const { companyName } = deleteTarget
      await api.deleteApplication(deleteTarget.id)
      setDeleteTarget(null)
      await refresh()
      toast.success(`${companyName} deleted.`)
    })

  const startNewApplication = () => {
    setFormPrefill(undefined)
    setFromPrefill(false)
    setFormTarget('new')
  }

  const viewProps = {
    applications: visible,
    matches,
    prepRuns,
    busyId,
    scoringId,
    onStatusChange: changeStatus,
    onScore: (application: Application) => void score(application),
    onOpenMatch: (application: Application) => setMatchTarget(application),
    onOpenPrep: (application: Application) =>
      setPrepTarget({ application, autoStart: false }),
    onOpenInterviews: (application: Application) => setInterviewsTarget(application),
    onOpenCoverLetter: (application: Application) => setCoverLetterTarget(application),
    onOpenInterviewPrep: (application: Application) => setInterviewPrepTarget(application),
    onEdit: (application: Application) => setFormTarget(application),
    onDelete: (application: Application) => setDeleteTarget(application),
  }

  return (
    <>
      <div className="space-y-4">
        <PageHeader
          title="Applications"
          description={
            applications.length === 1 ? '1 application' : `${applications.length} applications`
          }
          actions={
            <Button variant="primary" onClick={startNewApplication}>
              Add application
            </Button>
          }
        />

        <StatusFilter
          applications={applications}
          value={statusFilter}
          onChange={setStatusFilter}
        />

        <div className="flex flex-wrap items-center gap-3">
          <div className="w-full max-w-xs">
            <Field label="Search applications" labelHidden>
              {(props) => (
                <Input
                  {...props}
                  type="search"
                  placeholder="Search company or role…"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                />
              )}
            </Field>
          </div>
          {filtered && (
            <p className="text-sm text-fg-muted">
              Showing <span className="tabular-nums">{visible.length}</span> of{' '}
              <span className="tabular-nums">{applications.length}</span> ·{' '}
              <button
                type="button"
                onClick={clearFilters}
                className="rounded-sm font-medium text-accent hover:text-accent-hover"
              >
                Clear filters
              </button>
            </p>
          )}
        </div>

        {/* The only banner on this page, and only for a failed refresh. */}
        {loadError && applications.length > 0 && <Banner>{loadError}</Banner>}

        {loading ? (
          <LoadingState />
        ) : loadError && applications.length === 0 ? (
          /* Never show the "no applications yet" empty state on a failed load —
             an unreachable API must not look like lost data. */
          <Banner
            action={
              <Button
                size="sm"
                onClick={() => {
                  setLoading(true)
                  void refresh()
                }}
              >
                Try again
              </Button>
            }
          >
            {loadError} Your data is safe — the app just can't reach the server.
          </Banner>
        ) : applications.length === 0 ? (
          <EmptyState
            title="No applications yet"
            description="Add your first one to start tracking your pipeline."
            action={
              <Button variant="primary" onClick={startNewApplication}>
                Add application
              </Button>
            }
          />
        ) : visible.length === 0 ? (
          <EmptyState
            title="No applications match"
            description="Try a different search, or clear the status filter."
            action={<Button onClick={clearFilters}>Clear filters</Button>}
          />
        ) : (
          <>
            <div className="hidden md:block">
              <ApplicationsTable
                {...viewProps}
                sortKey={sortKey}
                sortAsc={sortAsc}
                onSort={sort}
              />
            </div>
            <div className="md:hidden">
              <ApplicationsList {...viewProps} />
            </div>
          </>
        )}
      </div>

      {matchTarget && matches[matchTarget.id] && (
        <MatchDetailModal
          application={matchTarget}
          match={matches[matchTarget.id]}
          resumes={resumes}
          rescoring={scoringId === matchTarget.id}
          tailoring={tailoring}
          onRescore={() => void score(matchTarget)}
          onLoadResumeContent={loadResumeContent}
          onTailorAndRescore={tailorAndRescore}
          onClose={() => setMatchTarget(null)}
        />
      )}

      {prepTarget && (
        <PrepModal
          application={prepTarget.application}
          run={prepRuns[prepTarget.application.id] ?? null}
          autoStart={prepTarget.autoStart}
          onRunChange={recordPrepRun}
          onMarkApplied={() => void markApplied()}
          onClose={() => {
            setPrepTarget(null)
            void refresh()
          }}
        />
      )}

      {interviewsTarget && (
        <InterviewsModal
          application={interviewsTarget}
          gmail={gmailStatus}
          onClose={() => setInterviewsTarget(null)}
          onChanged={() => void refresh()}
        />
      )}

      {coverLetterTarget && (
        <CoverLetterModal
          application={coverLetterTarget}
          onClose={() => setCoverLetterTarget(null)}
          onChanged={() => void refresh()}
        />
      )}

      {interviewPrepTarget && (
        <InterviewPrepModal
          application={interviewPrepTarget}
          onClose={() => setInterviewPrepTarget(null)}
        />
      )}

      {formTarget !== null && (
        <ApplicationFormModal
          application={formTarget === 'new' ? null : formTarget}
          prefill={formTarget === 'new' ? formPrefill : undefined}
          onSave={save}
          onClose={closeForm}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Delete application?"
          body={`This permanently removes ${deleteTarget.companyName} · ${deleteTarget.roleTitle}, including its status history, match scores, and any interviews on your Google Calendar.`}
          confirmLabel="Delete application"
          busyLabel="Deleting…"
          busy={deletion.busy}
          error={deletion.error}
          onConfirm={confirmDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </>
  )
}
