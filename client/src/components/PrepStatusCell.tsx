import type { PrepRun } from '../api/types'
import { Button, Spinner } from './ui'

interface Props {
  run: PrepRun | undefined
  hasJobDescription: boolean
  onOpen: () => void
}

/**
 * Every state is a link into the prep dialog, so the cell always answers
 * "and then what?". Labels come from the glossary — "Application prep",
 * never shortened to something that reads like interview prep.
 */
export function PrepStatusCell({ run, hasJobDescription, onOpen }: Props) {
  if (!hasJobDescription) {
    return (
      <span className="text-sm text-fg-subtle" title="Add the job description to enable prep">
        —
      </span>
    )
  }

  if (!run) {
    return (
      <Button size="sm" onClick={onOpen}>
        Start prep
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
        Prepping…
      </button>
    )
  }

  const label =
    run.status === 'Failed' ? 'Prep failed' : run.readyToApply ? 'Ready to apply' : 'Below target'

  return (
    <button
      type="button"
      onClick={onOpen}
      title={run.status === 'Failed' ? (run.errorMessage ?? undefined) : undefined}
      className={`rounded-control px-1.5 py-1 text-sm whitespace-nowrap transition-colors hover:bg-surface-muted ${
        run.status === 'Failed' ? 'text-danger' : run.readyToApply ? 'text-success' : 'text-fg-muted'
      }`}
    >
      {label}
    </button>
  )
}
