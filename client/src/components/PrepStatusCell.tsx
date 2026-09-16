import type { PrepRun } from '../api/types'
import { VERDICT_LABELS } from '../lib/tailoring'
import { Button, Spinner } from './ui'

interface Props {
  run: PrepRun | undefined
  hasJobDescription: boolean
  onOpen: () => void
}

/**
 * Where a job's tailoring stands, and always a way onto its page — which is
 * where tailoring starts, runs and reads back.
 */
export function PrepStatusCell({ run, hasJobDescription, onOpen }: Props) {
  if (!hasJobDescription) {
    return (
      <span className="text-sm text-fg-subtle" title="Add the job description to tailor for this job">
        —
      </span>
    )
  }

  if (!run) {
    return (
      <Button size="sm" onClick={onOpen}>
        Tailor
      </Button>
    )
  }

  if (run.status === 'Running') {
    return (
      <button
        type="button"
        onClick={onOpen}
        className="inline-flex items-center gap-2 rounded-control px-1.5 py-1 text-sm whitespace-nowrap text-fg-muted transition-colors hover:bg-surface-muted"
      >
        <Spinner />
        Tailoring…
      </button>
    )
  }

  const label =
    run.status === 'Failed'
      ? "Didn't finish"
      : run.review
        ? VERDICT_LABELS[run.review.verdict]
        : 'Tailor again'

  return (
    <button
      type="button"
      onClick={onOpen}
      title={run.status === 'Failed' ? (run.errorMessage ?? undefined) : undefined}
      className={`rounded-control px-1.5 py-1 text-sm whitespace-nowrap transition-colors hover:bg-surface-muted ${
        run.status === 'Failed' ? 'text-danger' : 'text-fg'
      }`}
    >
      {label}
    </button>
  )
}
