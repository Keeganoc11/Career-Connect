import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api/client'
import type {
  Application,
  ApplicationInput,
  ApplicationStatus,
  CopilotInsights,
  GmailConnectionStatus,
  GmailScanResult,
  MatchResult,
  ResumeSummary,
  SuggestedNewApplication,
  SuggestedStatusUpdate,
  Summary,
  TailorResumeInput,
} from '../api/types'
import { SummaryBar } from '../components/SummaryBar'
import { ApplicationsTable } from '../components/ApplicationsTable'
import { ApplicationFormModal } from '../components/ApplicationFormModal'
import { MatchDetailModal } from '../components/MatchDetailModal'
import { ConfirmDialog } from '../components/ConfirmDialog'
import { CopilotPanel } from '../components/CopilotPanel'
import { GmailConnectControl } from '../components/GmailConnectControl'
import { GmailSuggestionsModal } from '../components/GmailSuggestionsModal'
import { useApiErrorHandler } from '../lib/useApiErrorHandler'

export function TrackerPage({ onLoggedOut }: { onLoggedOut: () => void }) {
  const [applications, setApplications] = useState<Application[]>([])
  const [summary, setSummary] = useState<Summary | null>(null)
  const [matches, setMatches] = useState<Record<string, MatchResult>>({})
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [statusFilter, setStatusFilter] = useState<ApplicationStatus | null>(null)
  const [search, setSearch] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)
  const [scoringId, setScoringId] = useState<string | null>(null)
  const [scoreError, setScoreError] = useState<string | null>(null)
  const [matchTarget, setMatchTarget] = useState<Application | null>(null)
  const [resumes, setResumes] = useState<ResumeSummary[]>([])
  const [tailoring, setTailoring] = useState(false)
  const [formTarget, setFormTarget] = useState<Application | null | 'new'>(null)
  const [formPrefill, setFormPrefill] = useState<Partial<Pick<ApplicationInput, 'companyName' | 'roleTitle' | 'dateApplied'>> | undefined>(undefined)
  const [deleteTarget, setDeleteTarget] = useState<Application | null>(null)
  const [deleting, setDeleting] = useState(false)

  const [gmailStatus, setGmailStatus] = useState<GmailConnectionStatus | null>(null)
  const [gmailScanning, setGmailScanning] = useState(false)
  const [gmailBanner, setGmailBanner] = useState<{ tone: 'success' | 'error'; message: string } | null>(null)
  const [gmailScanResult, setGmailScanResult] = useState<GmailScanResult | null>(null)

  const [copilotInsights, setCopilotInsights] = useState<CopilotInsights | null>(null)
  const [copilotLoading, setCopilotLoading] = useState(false)
  const [copilotError, setCopilotError] = useState<string | null>(null)

  const handleError = useApiErrorHandler(onLoggedOut, setLoadError)

  const refresh = useCallback(async () => {
    try {
      const [list, counts, latestMatches, resumeList] = await Promise.all([
        api.listApplications(),
        api.getSummary(),
        api.listMatches(),
        api.listResumes(),
      ])
      setApplications(list)
      setSummary(counts)
      setMatches(latestMatches)
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
  }, [refresh])

  const loadGmailStatus = useCallback(async () => {
    try {
      setGmailStatus(await api.getGmailStatus())
    } catch (e) {
      handleError(e)
    }
  }, [handleError])

  useEffect(() => {
    void loadGmailStatus()
  }, [loadGmailStatus])

  // Land here after the Google OAuth redirect — read the outcome once, then
  // strip the query string so a page refresh doesn't re-show the banner.
  useEffect(() => {
    const params = new URLSearchParams(window.location.search)
    const gmailResult = params.get('gmail')
    if (!gmailResult) return

    if (gmailResult === 'connected') {
      setGmailBanner({ tone: 'success', message: 'Gmail connected.' })
      void loadGmailStatus()
    } else if (gmailResult === 'error') {
      setGmailBanner({
        tone: 'error',
        message: params.get('message') || 'Could not connect Gmail.',
      })
    }
    window.history.replaceState({}, '', window.location.pathname)
    // Intentionally runs once on mount — this reads the URL exactly once.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const connectGmail = async () => {
    try {
      window.location.href = await api.getGmailAuthorizationUrl()
    } catch (e) {
      handleError(e)
    }
  }

  const disconnectGmail = async () => {
    try {
      await api.disconnectGmail()
      await loadGmailStatus()
    } catch (e) {
      handleError(e)
    }
  }

  const scanGmail = async () => {
    setGmailScanning(true)
    setGmailBanner(null)
    try {
      const result = await api.scanGmail()
      await loadGmailStatus()
      if (result.statusUpdates.length === 0 && result.newApplications.length === 0) {
        setGmailBanner({ tone: 'success', message: 'No new updates found.' })
      } else {
        setGmailScanResult(result)
      }
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        handleError(e)
      } else {
        setGmailBanner({ tone: 'error', message: e instanceof Error ? e.message : 'Scan failed.' })
      }
    } finally {
      setGmailScanning(false)
    }
  }

  const acceptGmailSuggestion = async (suggestion: SuggestedStatusUpdate) => {
    await changeStatus(suggestion.applicationId, suggestion.suggestedStatus)
  }

  const reviewNewApplicationFromGmail = (suggestion: SuggestedNewApplication) => {
    setFormPrefill({
      companyName: suggestion.companyName,
      roleTitle: suggestion.roleTitle,
      dateApplied: suggestion.emailReceivedAtUtc.slice(0, 10),
    })
    setFormTarget('new')
  }

  const getCopilotInsights = async () => {
    setCopilotLoading(true)
    setCopilotError(null)
    try {
      setCopilotInsights(await api.getCopilotInsights())
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        handleError(e)
      } else {
        setCopilotError(e instanceof Error ? e.message : 'Could not get insights.')
      }
    } finally {
      setCopilotLoading(false)
    }
  }

  const dismissCopilot = () => {
    setCopilotInsights(null)
    setCopilotError(null)
  }

  const openApplicationFromCopilot = (application: Application) => {
    if (matches[application.id]) {
      setMatchTarget(application)
    } else {
      setFormTarget(application)
    }
  }

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

  const changeStatus = async (id: string, status: ApplicationStatus) => {
    setBusyId(id)
    try {
      await api.updateStatus(id, status)
      await refresh()
    } catch (e) {
      handleError(e)
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
      if (e instanceof ApiError && e.status === 401) {
        handleError(e)
      } else {
        setScoreError(e instanceof Error ? e.message : 'Scoring failed.')
      }
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

      const [result, resumeList] = await Promise.all([api.scoreMatch(matchTarget.id), api.listResumes()])
      setMatches((current) => ({ ...current, [matchTarget.id]: result }))
      setResumes(resumeList)
    } finally {
      setTailoring(false)
    }
  }

  const save = async (input: ApplicationInput) => {
    if (formTarget === 'new') {
      await api.createApplication(input)
    } else if (formTarget) {
      await api.updateApplication(formTarget.id, input)
    }
    setFormTarget(null)
    setFormPrefill(undefined)
    await refresh()
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      await api.deleteApplication(deleteTarget.id)
      setDeleteTarget(null)
      await refresh()
    } catch (e) {
      handleError(e)
    } finally {
      setDeleting(false)
    }
  }

  return (
    <>
      <main className="mx-auto max-w-6xl space-y-6 px-5 py-8">
        <div className="flex flex-wrap items-end justify-between gap-4">
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-slate-900">Applications</h1>
            <p className="mt-1.5 text-base text-slate-500">
              Every role you've applied to, and how well your resume fits each one.
            </p>
          </div>
          <button
            type="button"
            onClick={() => {
              setFormPrefill(undefined)
              setFormTarget('new')
            }}
            className="brand-gradient rounded-xl px-5 py-3 text-base font-semibold text-white shadow-lg shadow-brand-600/25 transition hover:opacity-95"
          >
            + Add application
          </button>
        </div>

        {summary && (
          <SummaryBar
            summary={summary}
            activeFilter={statusFilter}
            onFilterChange={setStatusFilter}
          />
        )}

        <CopilotPanel
          insights={copilotInsights}
          loading={copilotLoading}
          error={copilotError}
          applications={applications}
          onAnalyze={() => void getCopilotInsights()}
          onDismiss={dismissCopilot}
          onOpenApplication={openApplicationFromCopilot}
        />

        <div className="flex flex-wrap items-center justify-between gap-3">
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

          <GmailConnectControl
            status={gmailStatus}
            scanning={gmailScanning}
            onConnect={() => void connectGmail()}
            onScan={() => void scanGmail()}
            onDisconnect={() => void disconnectGmail()}
          />
        </div>

        {loadError && applications.length > 0 && (
          <p className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-base font-medium text-rose-700">
            {loadError}
          </p>
        )}

        {gmailBanner && (
          <div
            className={`flex items-start justify-between gap-3 rounded-xl border px-4 py-3 text-base font-medium ${
              gmailBanner.tone === 'success'
                ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
                : 'border-rose-200 bg-rose-50 text-rose-700'
            }`}
          >
            <span>{gmailBanner.message}</span>
            <button
              type="button"
              onClick={() => setGmailBanner(null)}
              aria-label="Dismiss"
              className="shrink-0 rounded-lg px-2 py-0.5 font-bold opacity-70 hover:opacity-100"
            >
              ✕
            </button>
          </div>
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
              Your data is safe on disk — this only means the app can't reach the server right now.
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
                onClick={() => {
                  setFormPrefill(undefined)
                  setFormTarget('new')
                }}
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
            busyId={busyId}
            scoringId={scoringId}
            onStatusChange={changeStatus}
            onScore={(application) => void score(application)}
            onOpenMatch={(application) => setMatchTarget(application)}
            onEdit={(application) => setFormTarget(application)}
            onDelete={(application) => setDeleteTarget(application)}
          />
        )}
      </main>

      {gmailScanResult && (
        <GmailSuggestionsModal
          statusUpdates={gmailScanResult.statusUpdates}
          newApplications={gmailScanResult.newApplications}
          onAcceptStatusUpdate={acceptGmailSuggestion}
          onAddNewApplication={reviewNewApplicationFromGmail}
          onClose={() => setGmailScanResult(null)}
        />
      )}

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

      {formTarget !== null && (
        <ApplicationFormModal
          application={formTarget === 'new' ? null : formTarget}
          prefill={formTarget === 'new' ? formPrefill : undefined}
          onSave={save}
          onClose={() => {
            setFormTarget(null)
            setFormPrefill(undefined)
          }}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Delete application?"
          body={`This permanently removes ${deleteTarget.companyName} — ${deleteTarget.roleTitle}, including its status history and match scores.`}
          confirmLabel="Delete"
          busy={deleting}
          onConfirm={() => void confirmDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </>
  )
}
