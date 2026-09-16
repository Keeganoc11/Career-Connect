import type { MatchResult } from '../api/types'
import { scoreBand } from '../lib/matchScore'
import { Button, Spinner } from './ui'

interface Props {
  match: MatchResult | undefined
  scoring: boolean
  hasJobDescription: boolean
  onScore: () => void
  onOpen: () => void
}

export function MatchScoreCell({ match, scoring, hasJobDescription, onScore, onOpen }: Props) {
  if (scoring) {
    return (
      <span className="inline-flex items-center gap-2 text-sm text-fg-muted">
        <Spinner />
        Scoring…
      </span>
    )
  }

  if (!match) {
    // Nothing to score against, so offer nothing — an em dash rather than a
    // button that would only explain why it can't run.
    if (!hasJobDescription) {
      return (
        <span className="text-sm text-fg-subtle" title="Add the job description to enable scoring">
          —
        </span>
      )
    }
    return (
      <Button size="sm" onClick={onScore}>
        Score
      </Button>
    )
  }

  const band = scoreBand(match.score)

  return (
    <button
      type="button"
      onClick={onOpen}
      className="flex w-28 flex-col gap-1 rounded-control p-1 text-left transition-colors hover:bg-surface-muted"
    >
      {/* Number over label rather than inline — inline wraps badly in a narrow
          column. */}
      <span className={`text-xl font-semibold leading-none tabular-nums ${band.text}`}>
        {match.score}
      </span>
      <span className="text-xs whitespace-nowrap text-fg-muted">{band.label}</span>
      <span className={`mt-0.5 h-1 w-full overflow-hidden rounded-full ${band.track}`}>
        <span
          className={`block h-full rounded-full ${band.bar}`}
          style={{ width: `${Math.max(match.score, 2)}%` }}
        />
      </span>
    </button>
  )
}
