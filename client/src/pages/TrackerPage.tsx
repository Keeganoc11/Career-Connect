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
  Summary,
  TailorResumeInput,
} from '../api/types'
import { SummaryBar } from '../components/SummaryBar'
import { ApplicationsTable } from '../components/ApplicationsTable'
import { ApplicationFormModal } from '../components/ApplicationFormModal'
import { MatchDetailModal } from '../components/MatchDetailModal'
import { PrepModal } from '../components/PrepModal'
import { CoverLetterModal } from '../components/CoverLetterModal'
import { InterviewPrepModal } from '../components/InterviewPrepModal'
import { InterviewsModal } from '../components/InterviewsModal'
import { ConfirmDialog, toast } from '../components/ui'
import { errorMessage } from '../lib/errors'
import { useAsyncAction } from '../lib/useAsyncAction'
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
  const [summary, setSummary] = useState<Summary | null>(null)
  const [matches, setMatches] = useState<Record<string, MatchResult>>({})
  const [prepRuns, setPrepRuns] = useState<Record<string, PrepRun>>({})
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [statusFilter, setStatusFilter] = useState<ApplicationStatus | null>(null)
  const [search, setSearch] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)
  const [scoringId, setScoringId] = useState<string | null>(null)
  const [scoreError, setScoreError] = useState<string | null>(null)
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
      const [list, counts, latestMatches, latestPrepRuns, resumeList] = await Promise.all([
        api.listApplications(),
        api.getSummary(),
        api.listMatches(),
        api.listPrepRuns(),
        api.listResumes(),
      ])
      setApplications(list)
      setSummary(counts)
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
   * Something elsewhere asked for a dialog — the header's Email updates, or
   * later the agenda. Resolved against the loaded list, so an intent that
   * arrives before the data does waits for it rather than being dropped.
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

  // A prep pass keeps running server-side after its modal is closed, so the
  // table polls for itself — otherwise a row would sit on "Prepping…" until
  // the next full page load.
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
    return applications.filter(
      (a) =>
        (statusFilter === null || a.status === statusFilter) &&
        (query === '' ||
          a.companyName.toLowerCase().includes(query) ||
          a.roleTitle.toLowerCase().includes(query)),
    )
  }, [applications, statusFilter, search])

  const changeStatus = async (id: string, status: ApplicationStatus): Promise<boolean> => {
    setBusyId(id)
    try {
      await api.updateStatus(id, status)
      await refresh()
      return true
    } catch (e) {
      handleError(e)
      return false
    } finally {
      setBusyId(null)
    }
  }

  const score = async (application: Application) => {
    setScoringId(application.id)
    setScoreError(null)
    try {
      const result = await api.scoreMatch(application.id)
      setMatches((current) => ({ ...current, [application.id]: result }))
      // Open the detail straight away — the score alone rarely answers
      // "so what should I change?", which is the point of running it.
      setMatchTarget(application)
    } catch (e) {
      setScoreError(errorMessage(e))
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
    // waiting to be found and clicked in the table.
    if (created && created.jobDescriptionText && created.status === 'Preparing') {
      setPrepTarget({ application: created, autoStart: true })
    }

    await refresh()
  }

  const recordPrepRun = useCallback((run: PrepRun) => {
    setPrepRuns((current) => ({ ...current, [run.applicationId]: run }))
  }, [])

  const openPrep = (application: Application) => {
    setPrepTarget({ application, autoStart: false })
  }

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

  return (
    <>
      <div className="space-y-6">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-slate-900">Applications</h1>
            <p className="mt-1.5 text-base text-slate-500">
              Every role you've applied to, and how well your resume fits each one.
            </p>
          </div>
          <button
            type="button"
            onClick={startNewApplication}
            className="brand-gradient rounded-xl px-5 py-3 text-base font-semibold text-white shadow-lg shadow-brand-600/25 transition hover:opacity-95"
          >
            + Add application
          </button>
        </div>

        {summary && (
          <SummaryBar summary={summary} activeFilter={statusFilter} onFilterChange={setStatusFilter} />
        )}

        <div className="flex flex-wrap items-center gap-3">
          <div className="relative w-full max-w-sm">
            <span
              className="pointer-events-none absolute left-4 top-1/2 -translate-y-1/2 text-slate-400"
              aria-hidden
            >
              ⌕
            </span>
            <input
              type="search"
              placeholder="Search company or role…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="w-full rounded-xl border border-slate-300 bg-white py-3 pl-10 pr-4 text-base placeholder:text-slate-400 focus:border-brand-500 focus:outline-none focus:ring-4 focus:ring-brand-500/15"
            />
          </div>
          <span className="text-sm font-medium text-slate-500">
            {visible.length} of {applications.length} shown
          </span>
        </div>

        {loadError && applications.length > 0 && (
          <p className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-base font-medium text-rose-700">
            {loadError}
          </p>
        )}

        {scoreError && (
          <div className="flex items-start justify-between gap-3 rounded-xl border border-amber-200 bg-amber-50 px-4 py-3 text-base text-amber-900">
            <span className="font-medium">{scoreError}</span>
            <button
              type="button"
              onClick={() => setScoreError(null)}
              aria-label="Dismiss"
              className="shrink-0 rounded-lg px-2 py-0.5 font-bold text-amber-700 hover:bg-amber-100"
            >
              ✕
            </button>
          </div>
        )}

        {loading ? (
          <p className="py-16 text-center text-base text-slate-500">Loading…</p>
        ) : loadError && applications.length === 0 ? (
          /* Never render the "no applications yet" empty state on a failed
             load — an unreachable API must not look like lost data. */
          <div className="rounded-2xl border-2 border-rose-200 bg-rose-50/60 py-16 text-center">
            <p className="text-lg font-bold text-rose-800">Couldn't load your applications</p>
            <p className="mx-auto mt-2 max-w-md text-base text-rose-700">{loadError}</p>
            <p className="mx-auto mt-3 max-w-md text-sm text-rose-600">
              Your data is safe — this only means the app can't reach the server right now.
            </p>
            <button
              type="button"
              onClick={() => {
                setLoading(true)
                void refresh()
              }}
              className="mt-5 rounded-xl bg-rose-600 px-5 py-2.5 text-base font-semibold text-white shadow-sm hover:bg-rose-500"
            >
              Try again
            </button>
          </div>
        ) : visible.length === 0 ? (
          <div className="rounded-2xl border-2 border-dashed border-slate-300 bg-white py-20 text-center">
            <p className="text-lg font-bold text-slate-700">
              {applications.length === 0 ? 'No applications yet' : 'Nothing matches your filters'}
            </p>
            <p className="mx-auto mt-2 max-w-md text-base text-slate-500">
              {applications.length === 0
                ? 'Add your first application to start tracking your pipeline.'
                : 'Try clearing the search or status filter.'}
            </p>
            {applications.length === 0 && (
              <button
                type="button"
                onClick={startNewApplication}
                className="brand-gradient mt-6 rounded-xl px-5 py-3 text-base font-semibold text-white shadow-lg shadow-brand-600/25"
              >
                + Add your first application
              </button>
            )}
          </div>
        ) : (
          <ApplicationsTable
            applications={visible}
            matches={matches}
            prepRuns={prepRuns}
            busyId={busyId}
            scoringId={scoringId}
            onStatusChange={changeStatus}
            onScore={(application) => void score(application)}
            onOpenMatch={(application) => setMatchTarget(application)}
            onOpenPrep={openPrep}
            onOpenInterviews={(application) => setInterviewsTarget(application)}
            onOpenCoverLetter={(application) => setCoverLetterTarget(application)}
            onOpenInterviewPrep={(application) => setInterviewPrepTarget(application)}
            onEdit={(application) => setFormTarget(application)}
            onDelete={(application) => setDeleteTarget(application)}
          />
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
