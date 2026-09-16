import type { MatchResult } from '../api/types'
import { scoreBand } from '../lib/matchScore'
interface Props {
  match: MatchResult | undefined
  onOpen: () => void
}

/**
 * The latest score, which tailoring records as it goes. Scoring on its own is
 * gone — it happens as part of tailoring, on the job's page.
 */
export function MatchScoreCell({ match, onOpen }: Props) {
  if (!match) {
    return <span className="text-sm text-fg-subtle">—</span>
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
