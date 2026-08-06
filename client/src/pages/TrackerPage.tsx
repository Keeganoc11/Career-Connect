import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, auth, ApiError } from '../api/client'
import type {
  Application,
  ApplicationInput,
  ApplicationStatus,
  MatchResult,
  Summary,
} from '../api/types'
import { SummaryBar } from '../components/SummaryBar'
import { ApplicationsTable } from '../components/ApplicationsTable'
import { ApplicationFormModal } from '../components/ApplicationFormModal'
import { MatchDetailModal } from '../components/MatchDetailModal'
import { ConfirmDialog } from '../components/ConfirmDialog'

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
  const [formTarget, setFormTarget] = useState<Application | null | 'new'>(null)
  const [deleteTarget, setDeleteTarget] = useState<Application | null>(null)
  const [deleting, setDeleting] = useState(false)

  const handleError = useCallback(
    (e: unknown) => {
      if (e instanceof ApiError && e.status === 401) {
        auth.clear()
        onLoggedOut()
        return
      }
      setLoadError(e instanceof Error ? e.message : 'Something went wrong.')
    },
    [onLoggedOut],
  )

  const refresh = useCallback(async () => {
    try {
      const [list, counts, latestMatches] = await Promise.all([
        api.listApplications(),
        api.getSummary(),
        api.listMatches(),
      ])
      setApplications(list)
      setSummary(counts)
      setMatches(latestMatches)
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

  const save = async (input: ApplicationInput) => {
    if (formTarget === 'new') {
      await api.createApplication(input)
    } else if (formTarget) {
      await api.updateApplication(formTarget.id, input)
    }
    setFormTarget(null)
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
            onClick={() => setFormTarget('new')}
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
                onClick={() => setFormTarget('new')}
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

      {matchTarget && matches[matchTarget.id] && (
        <MatchDetailModal
          application={matchTarget}
          match={matches[matchTarget.id]}
          rescoring={scoringId === matchTarget.id}
          onRescore={() => void score(matchTarget)}
          onClose={() => setMatchTarget(null)}
        />
      )}

      {formTarget !== null && (
        <ApplicationFormModal
          application={formTarget === 'new' ? null : formTarget}
          onSave={save}
          onClose={() => setFormTarget(null)}
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
