import { ClipboardCheck } from 'lucide-react'
import type { InterviewDebrief } from '../api/types'
import { formatRelative } from '../lib/format'
import { debriefBand } from '../lib/interviewQuestions'
import { Button, Card, EmptyState } from './ui'

interface Props {
  debrief: InterviewDebrief | null
  busy: boolean
  /** Null when there's nothing to score yet — the reason is shown instead of a dead button. */
  blockedReason: string | null
  onGenerate: () => void
}

/**
 * The honest read on a round, in the same shape as the resume reality check:
 * a score with a band, a blunt verdict, then what held up and what didn't.
 */
export function InterviewDebriefPanel({ debrief, busy, blockedReason, onGenerate }: Props) {
  if (!debrief) {
    return (
      <Card>
        <EmptyState
          icon={<ClipboardCheck className="size-5" aria-hidden />}
          title="No debrief yet"
          description={
            blockedReason ??
            'Scores this round against the job description: what you evidenced, what it exposed, and what to rehearse before the next one.'
          }
          action={
            blockedReason ? undefined : (
              <Button variant="primary" loading={busy} onClick={onGenerate}>
                Debrief this round
              </Button>
            )
          }
        />
      </Card>
    )
  }

  const band = debriefBand(debrief.score)

  return (
    <Card>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="text-base font-semibold text-fg">Debrief</h2>
          <p className="mt-0.5 text-sm text-fg-muted">
            Scored against the job description · {formatRelative(debrief.generatedAtUtc)}
          </p>
        </div>
        <Button loading={busy} onClick={onGenerate}>
          Redo
        </Button>
      </div>

      <div className="mt-4 flex items-end gap-3">
        <span className={`text-4xl leading-none font-semibold tabular-nums ${band.text}`}>
          {debrief.score}
        </span>
        <span className="pb-1 text-sm text-fg-muted">{band.label}</span>
      </div>
      <span className={`mt-2 block h-1 w-full overflow-hidden rounded-full ${band.track}`}>
        <span
          className={`block h-full rounded-full ${band.bar}`}
          style={{ width: `${Math.max(debrief.score, 2)}%` }}
        />
      </span>

      <p className="mt-4 text-sm leading-relaxed whitespace-pre-wrap text-fg">{debrief.verdict}</p>

      {debrief.covered.length > 0 && (
        <section className="mt-5">
          <h3 className="text-sm font-semibold text-fg">What you showed</h3>
          <ul className="mt-2 space-y-2">
            {debrief.covered.map((item) => (
              <li key={item.requirement} className="text-sm">
                <span className="font-medium text-fg">{item.requirement}</span>
                <span className="block text-fg-muted">{item.evidence}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {debrief.gaps.length > 0 && (
        <section className="mt-5">
          <h3 className="text-sm font-semibold text-fg">What it exposed</h3>
          <ul className="mt-2 space-y-3">
            {debrief.gaps.map((gap) => (
              <li key={gap.requirement} className="text-sm">
                <span className="font-medium text-fg">{gap.requirement}</span>
                <span className="block text-fg-muted">{gap.whatHappened}</span>
                <span className="mt-0.5 block text-fg">{gap.fix}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {debrief.practice.length > 0 && (
        <section className="mt-5">
          <h3 className="text-sm font-semibold text-fg">Rehearse these</h3>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-fg-muted marker:text-fg-subtle">
            {debrief.practice.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </section>
      )}

      {debrief.nextRound.length > 0 && (
        <section className="mt-5">
          <h3 className="text-sm font-semibold text-fg">Before the next round</h3>
          <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-fg-muted marker:text-fg-subtle">
            {debrief.nextRound.map((item) => (
              <li key={item}>{item}</li>
            ))}
          </ul>
        </section>
      )}
    </Card>
  )
}
