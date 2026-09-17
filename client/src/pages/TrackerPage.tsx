import { useCallback, useEffect, useMemo, useState } from 'react'
import { api } from '../api/client'
import type {
  Application,
  ApplicationInput,
  ApplicationStatus,
  GmailConnectionStatus,
  MatchResult,
  PrepRun,
} from '../api/types'
import { ApplicationsTable } from '../components/ApplicationsTable'
import { ApplicationsList } from '../components/ApplicationsList'
import { StatusFilter } from '../components/StatusFilter'
import { ApplicationFormModal } from '../components/ApplicationFormModal'
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
import { usePlan } from '../lib/planContext'
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
  onOpenJob: (applicationId: string) => void
  /**
   * This page's dialogs also open over a job's page, which keeps its own copy
   * of the data — so every change here tells the rest of the app to refetch.
   */
  onDataChanged: () => void
  onDeleted: (applicationId: string) => void
}

export function TrackerPage({
  dataVersion,
  intent,
  onIntentHandled,
  onPrefilledSave,
  gmailStatus,
  onOpenJob,
  onDataChanged,
  onDeleted,
}: Props) {
  const { isPro } = usePlan()
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
  const [coverLetterTarget, setCoverLetterTarget] = useState<Application | null>(null)
  const [interviewPrepTarget, setInterviewPrepTarget] = useState<Application | null>(null)
  const [interviewsTarget, setInterviewsTarget] = useState<Application | null>(null)
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
      // Scores and prep runs only exist on Pro, and both endpoints answer 402
      // there — asking anyway would fail the whole load.
      const [list, latestMatches, latestPrepRuns] = await Promise.all([
        api.listApplications(),
        isPro ? api.listMatches() : Promise.resolve({}),
        isPro ? api.listPrepRuns() : Promise.resolve({}),
      ])
      setApplications(list)
      setMatches(latestMatches)
      setPrepRuns(latestPrepRuns)
      setLoadError(null)
    } catch (e) {
      handleError(e)
    } finally {
      setLoading(false)
    }
  }, [handleError, isPro])

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
      case 'delete':
        setDeleteTarget(application)
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
    }
    onIntentHandled()
  }, [intent, applications, onIntentHandled])

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
      onDataChanged()
      return true
    } catch (e) {
      toast.error(errorMessage(e) ?? 'Could not update that status.')
      return false
    } finally {
      setBusyId(null)
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

    await refresh()
    onDataChanged()

    // The whole point of capturing a posting is to tailor for it, so a new one
    // with a description goes straight to its page with tailoring under way.
    if (isPro && created && created.jobDescriptionText && created.status === 'Preparing') {
      try {
        await api.startPrep(created.id)
      } catch (e) {
        toast.info(errorMessage(e) ?? 'Tailoring couldn’t start.')
      }
      onOpenJob(created.id)
    }
  }

  // The dialog stays open and shows the reason if this fails, rather than
  // closing as though the delete worked.
  const confirmDelete = () =>
    void deletion.run(async () => {
      if (!deleteTarget) return
      const { companyName, id } = deleteTarget
      await api.deleteApplication(id)
      setDeleteTarget(null)
      await refresh()
      onDeleted(id)
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
    onStatusChange: changeStatus,
    onOpenJob: (application: Application) => onOpenJob(application.id),
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

      {interviewsTarget && (
        <InterviewsModal
          application={interviewsTarget}
          gmail={gmailStatus}
          onClose={() => setInterviewsTarget(null)}
          onChanged={() => {
            void refresh()
            onDataChanged()
          }}
        />
      )}

      {coverLetterTarget && (
        <CoverLetterModal
          application={coverLetterTarget}
          onClose={() => setCoverLetterTarget(null)}
          onChanged={() => {
            void refresh()
            onDataChanged()
          }}
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
