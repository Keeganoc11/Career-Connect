import { useCallback, useEffect, useState } from 'react'
import { Archive, CalendarClock, Clock, FileText, Send } from 'lucide-react'
import { api } from '../api/client'
import type { Agenda, AgendaNudge, NudgeKind, UpcomingInterview } from '../api/types'
import { formatDateTime, formatUntil } from '../lib/format'
import { KIND_LABELS } from '../lib/interviews'
import { errorMessage } from '../lib/errors'
import type { TrackerIntentRequest } from '../lib/trackerIntent'
import { ActivityFeed } from '../components/ActivityFeed'
import { FollowUpModal } from '../components/FollowUpModal'
import { PipelineReview } from '../components/PipelineReview'
import { usePlan } from '../lib/planContext'
import {
  Badge,
  Banner,
  Button,
  Card,
  EmptyState,
  LoadingState,
  PageHeader,
  toast,
} from '../components/ui'

interface Props {
  dataVersion: number
  onOpenJob: (applicationId: string) => void
  /** Opens one of the tracker's dialogs over whichever page is showing. */
  onIntent: (request: TrackerIntentRequest) => void
  /** Something here changed an application, so every page should refetch. */
  onDataChanged: () => void
}

const SOON_MS = 48 * 3_600_000

/**
 * One action per nudge, done from here. Every nudge used to be a button that
 * just switched to the applications tab and left you to find the row again.
 */
const NUDGE_ACTIONS: Record<
  NudgeKind,
  {
    icon: typeof Clock
    label: string
    /** Opens a dialog over the agenda. */
    request?: (applicationId: string) => TrackerIntentRequest
    /** Goes to the job's page instead. */
    openJob?: boolean
    /** Drafts a follow-up email right here. */
    followUp?: boolean
  }
> = {
  ReadyToApply: { icon: Send, label: 'Open', openJob: true },
  NeverPrepped: { icon: FileText, label: 'Tailor resume', openJob: true },
  AwaitingYou: {
    icon: CalendarClock,
    label: 'Schedule interview',
    request: (id) => ({ kind: 'interviews', applicationId: id }),
  },
  Silent: { icon: Clock, label: 'Draft follow-up', followUp: true },
  ProbablyGhosted: { icon: Archive, label: 'Mark as ghosted' },
}

export function AgendaPage({ dataVersion, onOpenJob, onIntent, onDataChanged }: Props) {
  const { isPro } = usePlan()
  const [agenda, setAgenda] = useState<Agenda | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [ghostingId, setGhostingId] = useState<string | null>(null)
  const [followUpTarget, setFollowUpTarget] = useState<AgendaNudge | null>(null)

  // Signing out on a 401 is handled once, inside api/client.
  const handleError = useCallback((e: unknown) => setError(errorMessage(e)), [])

  const refresh = useCallback(async () => {
    try {
      setAgenda(await api.getAgenda())
      setError(null)
    } catch (e) {
      handleError(e)
    } finally {
      setLoading(false)
    }
  }, [handleError])

  // dataVersion changes when an email update moved something the agenda shows.
  useEffect(() => {
    void refresh()
  }, [refresh, dataVersion])

  const markGhosted = async (nudge: AgendaNudge) => {
    setGhostingId(nudge.applicationId)
    try {
      await api.updateStatus(nudge.applicationId, 'Ghosted')
      await refresh()
      onDataChanged()
      toast.success(`${nudge.companyName} marked as ghosted.`)
    } catch (e) {
      toast.error(errorMessage(e) ?? 'Could not update that application.')
    } finally {
      setGhostingId(null)
    }
  }

  const nothingToDo =
    agenda !== null && agenda.upcomingInterviews.length === 0 && agenda.nudges.length === 0

  return (
    <div className="space-y-8">
      <PageHeader title="Agenda" description="What's coming up, what needs a nudge, and what was handled for you." />

      {error && (
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
          {error}
        </Banner>
      )}

      {loading && !agenda && <LoadingState />}

      {nothingToDo && (
        <EmptyState
          title="Nothing needs you right now"
          description="No interviews booked and nothing has gone stale."
          action={<Button onClick={() => onIntent({ kind: 'new' })}>Add an application</Button>}
        />
      )}

      {agenda && agenda.upcomingInterviews.length > 0 && (
        <section>
          <h2 className="mb-2 text-base font-semibold text-fg">Coming up</h2>
          <ul className="grid gap-3 sm:grid-cols-2">
            {agenda.upcomingInterviews.map((interview) => (
              <InterviewCard key={interview.interviewId} interview={interview} onIntent={onIntent} />
            ))}
          </ul>
        </section>
      )}

      {agenda && agenda.nudges.length > 0 && (
        <section>
          <h2 className="mb-2 text-base font-semibold text-fg">Needs attention</h2>
          <Card padded={false}>
            <ul className="divide-y divide-line">
              {agenda.nudges.map((nudge) => (
                <NudgeRow
                  key={nudge.applicationId}
                  nudge={nudge}
                  busy={ghostingId === nudge.applicationId}
                  onIntent={onIntent}
                  onOpenJob={onOpenJob}
                  isPro={isPro}
                  onFollowUp={() => setFollowUpTarget(nudge)}
                  onMarkGhosted={() => void markGhosted(nudge)}
                />
              ))}
            </ul>
          </Card>
        </section>
      )}

      <ActivityFeed dataVersion={dataVersion} onDataChanged={onDataChanged} onOpenJob={onOpenJob} />

      {/* Reads every open application with a model call — Pro only. */}
      {isPro && <PipelineReview onOpenApplication={onOpenJob} />}

      {followUpTarget && (
        <FollowUpModal
          applicationId={followUpTarget.applicationId}
          companyName={followUpTarget.companyName}
          roleTitle={followUpTarget.roleTitle}
          onSent={() => {
            void refresh()
            onDataChanged()
          }}
          onClose={() => setFollowUpTarget(null)}
        />
      )}
    </div>
  )
}

function InterviewCard({
  interview,
  onIntent,
}: {
  interview: UpcomingInterview
  onIntent: (request: TrackerIntentRequest) => void
}) {
  const soon = new Date(interview.scheduledAtUtc).getTime() - Date.now() < SOON_MS

  return (
    <li>
      <Card raised>
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <p className="text-sm font-medium text-fg">{interview.companyName}</p>
          <p className="text-xs text-fg-muted">{formatUntil(interview.scheduledAtUtc)}</p>
        </div>
        <p className="text-sm text-fg-muted">{interview.roleTitle}</p>
        <p className="mt-2 text-sm text-fg">
          {KIND_LABELS[interview.kind]} · {formatDateTime(interview.scheduledAtUtc)}
        </p>

        <div className="mt-2 flex flex-wrap gap-1.5">
          {/* Only urgent once it's close — said here rather than repeated as a
              separate nudge, which duplicated the card. */}
          {soon && <Badge emphasis="interview">Soon</Badge>}
          {interview.onCalendar && <Badge>On Google Calendar</Badge>}
          <Badge>{interview.hasPrep ? 'Prep ready' : 'No prep yet'}</Badge>
        </div>

        {interview.notes && (
          <p className="mt-2 whitespace-pre-wrap text-sm text-fg-muted">{interview.notes}</p>
        )}

        <div className="mt-3 flex flex-wrap gap-2">
          <Button
            size="sm"
            variant={interview.hasPrep ? 'secondary' : 'primary'}
            onClick={() =>
              onIntent({ kind: 'interviewPrep', applicationId: interview.applicationId })
            }
          >
            {interview.hasPrep ? 'View interview prep' : 'Prepare'}
          </Button>
          <Button
            size="sm"
            onClick={() => onIntent({ kind: 'interviews', applicationId: interview.applicationId })}
          >
            Details
          </Button>
        </div>
      </Card>
    </li>
  )
}

function NudgeRow({
  nudge,
  busy,
  isPro,
  onIntent,
  onOpenJob,
  onFollowUp,
  onMarkGhosted,
}: {
  nudge: AgendaNudge
  busy: boolean
  isPro: boolean
  onIntent: (request: TrackerIntentRequest) => void
  onOpenJob: (applicationId: string) => void
  onFollowUp: () => void
  onMarkGhosted: () => void
}) {
  const action = NUDGE_ACTIONS[nudge.kind] ?? NUDGE_ACTIONS.Silent
  const Icon = action.icon

  // The nudge itself is fine on Free — a job has gone quiet either way — but
  // writing the follow-up is the Pro part, so it opens the job instead.
  const writesFollowUp = Boolean(action.followUp) && isPro

  return (
    <li className="flex items-start gap-3 p-4">
      <Icon className="mt-0.5 size-4 shrink-0 text-fg-subtle" aria-hidden />
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium text-fg">
          {nudge.companyName} <span className="font-normal text-fg-muted">· {nudge.roleTitle}</span>
        </p>
        <p className="mt-0.5 text-sm text-fg-muted">{nudge.message}</p>
      </div>
      <Button
        size="sm"
        loading={busy}
        onClick={() => {
          const request = action.request?.(nudge.applicationId)
          if (writesFollowUp) onFollowUp()
          else if (action.followUp || action.openJob) onOpenJob(nudge.applicationId)
          else if (request) onIntent(request)
          else onMarkGhosted()
        }}
      >
        {writesFollowUp || !action.followUp ? action.label : 'Open'}
      </Button>
    </li>
  )
}
