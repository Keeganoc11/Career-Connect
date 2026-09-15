import { useCallback, useEffect, useState } from 'react'
import { api } from '../api/client'
import type { Agenda, AgendaNudge, InterviewKind, NudgeKind, UpcomingInterview } from '../api/types'
import { formatDateTime, formatUntil } from '../lib/format'
import { useApiErrorHandler } from '../lib/useApiErrorHandler'

const KIND_LABELS: Record<InterviewKind, string> = {
  PhoneScreen: 'Phone screen',
  Technical: 'Technical',
  Onsite: 'Onsite',
  Final: 'Final round',
  Other: 'Interview',
}

/** Tone carries the urgency, so a glance down the list reads as a priority order. */
const NUDGE_STYLES: Record<NudgeKind, { icon: string; ring: string; text: string }> = {
  ReadyToApply: { icon: '🚀', ring: 'ring-emerald-600/20 bg-emerald-50', text: 'text-emerald-900' },
  AwaitingYou: { icon: '⏳', ring: 'ring-brand-600/20 bg-brand-50', text: 'text-brand-900' },
  Silent: { icon: '📭', ring: 'ring-slate-300 bg-white', text: 'text-slate-800' },
  ProbablyGhosted: { icon: '👻', ring: 'ring-slate-300 bg-slate-50', text: 'text-slate-600' },
  NeverPrepped: { icon: '📝', ring: 'ring-slate-300 bg-white', text: 'text-slate-800' },
}

interface Props {
  onLoggedOut: () => void
  onOpenTracker: () => void
}

function InterviewCard({ interview }: { interview: UpcomingInterview }) {
  const soon = new Date(interview.scheduledAtUtc).getTime() - Date.now() < 48 * 3_600_000

  return (
    <li
      className={`rounded-2xl bg-white p-5 shadow-sm ring-1 ${
        soon ? 'ring-2 ring-fuchsia-400' : 'ring-slate-200'
      }`}
    >
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <p className="text-lg font-bold text-slate-900">{interview.companyName}</p>
        <p className={`text-sm font-bold ${soon ? 'text-fuchsia-700' : 'text-slate-500'}`}>
          {formatUntil(interview.scheduledAtUtc)}
        </p>
      </div>
      <p className="mt-0.5 text-sm text-slate-500">{interview.roleTitle}</p>
      <p className="mt-2 text-sm font-semibold text-slate-700">
        {KIND_LABELS[interview.kind]} · {formatDateTime(interview.scheduledAtUtc)}
      </p>
      {interview.notes && (
        <p className="mt-2 text-sm whitespace-pre-wrap text-slate-600">{interview.notes}</p>
      )}
      <div className="mt-3 flex flex-wrap gap-2 text-xs font-bold">
        {interview.onCalendar && (
          <span className="rounded-full bg-sky-50 px-2.5 py-0.5 text-sky-800 ring-1 ring-sky-600/20 ring-inset">
            On your calendar
          </span>
        )}
        {/* Unprepped only reads as urgent once the interview is close — this is
            the one place that says so, rather than repeating it as a nudge. */}
        <span
          className={`rounded-full px-2.5 py-0.5 ring-1 ring-inset ${
            interview.hasPrep
              ? 'bg-emerald-50 text-emerald-800 ring-emerald-600/20'
              : soon
                ? 'bg-amber-100 text-amber-900 ring-amber-600/40'
                : 'bg-slate-50 text-slate-600 ring-slate-300'
          }`}
        >
          {interview.hasPrep ? 'Prep ready' : soon ? '⚠️ No prep yet' : 'No prep yet'}
        </span>
      </div>
    </li>
  )
}

function NudgeRow({ nudge, onOpen }: { nudge: AgendaNudge; onOpen: () => void }) {
  const style = NUDGE_STYLES[nudge.kind]
  return (
    <li>
      <button
        type="button"
        onClick={onOpen}
        className={`flex w-full items-start gap-3 rounded-2xl px-5 py-4 text-left shadow-sm ring-1 transition hover:shadow-md ${style.ring}`}
      >
        <span aria-hidden className="text-lg leading-none">
          {style.icon}
        </span>
        <span className="min-w-0 flex-1">
          <span className="block font-bold text-slate-900">
            {nudge.companyName} <span className="font-medium text-slate-500">· {nudge.roleTitle}</span>
          </span>
          <span className={`mt-0.5 block text-sm ${style.text}`}>{nudge.message}</span>
        </span>
      </button>
    </li>
  )
}

export function AgendaPage({ onLoggedOut, onOpenTracker }: Props) {
  const [agenda, setAgenda] = useState<Agenda | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const handleError = useApiErrorHandler(onLoggedOut, setError)

  const refresh = useCallback(async () => {
    setLoading(true)
    try {
      setAgenda(await api.getAgenda())
      setError(null)
    } catch (e) {
      handleError(e)
    } finally {
      setLoading(false)
    }
  }, [handleError])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const nothingToDo =
    agenda && agenda.upcomingInterviews.length === 0 && agenda.nudges.length === 0

  return (
    <main className="mx-auto max-w-6xl space-y-8 px-5 py-8">
      <div>
        <h1 className="text-3xl font-bold tracking-tight text-slate-900">Agenda</h1>
        <p className="mt-1.5 text-base text-slate-500">
          What's coming up, and what's gone quiet.
        </p>
      </div>

      {error && (
        <p className="rounded-2xl border border-rose-200 bg-rose-50 px-5 py-4 text-rose-700">{error}</p>
      )}

      {loading && !agenda && <p className="text-slate-500">Loading…</p>}

      {nothingToDo && (
        <div className="rounded-2xl border-2 border-dashed border-slate-300 px-6 py-12 text-center">
          <p className="text-lg font-semibold text-slate-700">Nothing needs you right now.</p>
          <p className="mt-1.5 text-slate-500">
            No interviews booked and nothing has gone stale.{' '}
            <button
              type="button"
              onClick={onOpenTracker}
              className="font-semibold text-brand-600 hover:underline"
            >
              Add an application
            </button>{' '}
            to get going.
          </p>
        </div>
      )}

      {agenda && agenda.upcomingInterviews.length > 0 && (
        <section className="space-y-3">
          <h2 className="text-sm font-bold tracking-wide text-slate-500 uppercase">
            Coming up
          </h2>
          <ul className="grid gap-4 sm:grid-cols-2">
            {agenda.upcomingInterviews.map((interview) => (
              <InterviewCard key={interview.interviewId} interview={interview} />
            ))}
          </ul>
        </section>
      )}

      {agenda && agenda.nudges.length > 0 && (
        <section className="space-y-3">
          <h2 className="text-sm font-bold tracking-wide text-slate-500 uppercase">
            Needs attention
          </h2>
          <ul className="space-y-2">
            {agenda.nudges.map((nudge) => (
              <NudgeRow key={nudge.applicationId} nudge={nudge} onOpen={onOpenTracker} />
            ))}
          </ul>
        </section>
      )}
    </main>
  )
}
