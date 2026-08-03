import { useCallback, useEffect, useMemo, useState } from 'react'
import { api, auth, ApiError } from '../api/client'
import type { Application, ApplicationInput, ApplicationStatus, Summary } from '../api/types'
import { SummaryBar } from '../components/SummaryBar'
import { ApplicationsTable } from '../components/ApplicationsTable'
import { ApplicationFormModal } from '../components/ApplicationFormModal'
import { ConfirmDialog } from '../components/ConfirmDialog'

export function TrackerPage({ onLoggedOut }: { onLoggedOut: () => void }) {
  const [applications, setApplications] = useState<Application[]>([])
  const [summary, setSummary] = useState<Summary | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)

  const [statusFilter, setStatusFilter] = useState<ApplicationStatus | null>(null)
  const [search, setSearch] = useState('')
  const [busyId, setBusyId] = useState<string | null>(null)
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
      const [list, counts] = await Promise.all([api.listApplications(), api.getSummary()])
      setApplications(list)
      setSummary(counts)
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
    <div className="min-h-screen">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-6xl items-center justify-between px-4 py-3">
          <div className="flex items-center gap-2.5">
            <div className="flex size-8 items-center justify-center rounded-lg bg-indigo-600 text-sm font-bold text-white">
              CC
            </div>
            <span className="text-base font-semibold text-slate-900">Career Connect</span>
          </div>
          <div className="flex items-center gap-3 text-sm">
            <span className="hidden text-slate-500 sm:inline">{auth.email}</span>
            <button
              type="button"
              onClick={() => {
                auth.clear()
                onLoggedOut()
              }}
              className="rounded-lg px-3 py-1.5 font-medium text-slate-600 hover:bg-slate-100"
            >
              Sign out
            </button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-6xl space-y-5 px-4 py-6">
        {summary && (
          <SummaryBar summary={summary} activeFilter={statusFilter} onFilterChange={setStatusFilter} />
        )}

        <div className="flex flex-wrap items-center justify-between gap-3">
          <input
            type="search"
            placeholder="Search company or role…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            className="w-full max-w-xs rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm placeholder:text-slate-400 focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30"
          />
          <button
            type="button"
            onClick={() => setFormTarget('new')}
            className="rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500"
          >
            + Add application
          </button>
        </div>

        {loadError && (
          <p className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">{loadError}</p>
        )}

        {loading ? (
          <p className="py-12 text-center text-sm text-slate-500">Loading…</p>
        ) : visible.length === 0 ? (
          <div className="rounded-xl border border-dashed border-slate-300 bg-white py-16 text-center">
            <p className="text-sm font-medium text-slate-700">
              {applications.length === 0 ? 'No applications yet' : 'Nothing matches your filters'}
            </p>
            <p className="mt-1 text-sm text-slate-500">
              {applications.length === 0
                ? 'Add your first application to start tracking your pipeline.'
                : 'Try clearing the search or status filter.'}
            </p>
          </div>
        ) : (
          <ApplicationsTable
            applications={visible}
            busyId={busyId}
            onStatusChange={changeStatus}
            onEdit={(application) => setFormTarget(application)}
            onDelete={(application) => setDeleteTarget(application)}
          />
        )}
      </main>

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
          body={`This permanently removes ${deleteTarget.companyName} — ${deleteTarget.roleTitle}, including its status history.`}
          confirmLabel="Delete"
          busy={deleting}
          onConfirm={() => void confirmDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </div>
  )
}
