import type { MatchResult } from '../api/types'
import { scoreBand } from '../lib/matchScore'

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
      <span className="inline-flex items-center gap-2 text-sm font-medium text-brand-600">
        <span className="size-4 animate-spin rounded-full border-2 border-brand-200 border-t-brand-600" />
        Scoring…
      </span>
    )
  }

  if (!match) {
    if (!hasJobDescription) {
      return (
        <span
          className="text-sm text-slate-400"
          title="Add the job description to this application to enable scoring"
        >
          No description
        </span>
      )
    }
    return (
      <button
        type="button"
        onClick={onScore}
        className="rounded-lg border-2 border-brand-200 bg-brand-50 px-3 py-1.5 text-sm font-semibold text-brand-700 transition hover:border-brand-300 hover:bg-brand-100"
      >
        Score match
      </button>
    )
  }

  const band = scoreBand(match.score)

  return (
    <button
      type="button"
      onClick={onOpen}
      title={`${band.label} — click for details`}
      className="group/score flex w-28 flex-col gap-1 rounded-lg p-1 text-left transition hover:bg-white"
    >
      {/* Number and label stack rather than sit inline — inline wraps badly in
          a narrow column. */}
      <span className={`text-2xl font-bold leading-none tabular-nums ${band.text}`}>
        {match.score}
      </span>
      <span className="text-sm font-medium whitespace-nowrap text-slate-500 group-hover/score:text-slate-700">
        {band.label}
      </span>
      <span className={`mt-0.5 h-1.5 w-full overflow-hidden rounded-full ${band.track}`}>
        <span
          className={`block h-full rounded-full transition-all ${band.bar}`}
          style={{ width: `${Math.max(match.score, 2)}%` }}
        />
      </span>
    </button>
  )
}
