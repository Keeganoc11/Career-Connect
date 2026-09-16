import { Check } from 'lucide-react'
import type { PrepRun } from '../api/types'
import { Badge, Spinner } from './ui'

/** The steps a tailoring pass took, live while it runs. */
export function PrepProgress({ run }: { run: PrepRun }) {
  const running = run.status === 'Running'

  return (
    <ol className="space-y-2">
      {run.steps.map((step, index) => (
        <li
          key={index}
          className="flex items-start gap-2.5 rounded-control border border-line px-3.5 py-3"
        >
          <Check className="mt-0.5 size-4 shrink-0 text-success" aria-hidden />
          <div className="min-w-0 flex-1">
            <p className="text-sm font-medium text-fg">{step.label}</p>
            <p className="mt-0.5 text-sm text-fg-muted">{step.detail}</p>
          </div>
          {step.score !== null && (
            <Badge emphasis={step.score >= run.targetScore ? 'accent' : 'neutral'}>
              <span className="tabular-nums">{step.score}</span>
            </Badge>
          )}
        </li>
      ))}
      {running && (
        <li className="flex items-center gap-2.5 rounded-control border border-line px-3.5 py-3">
          <Spinner />
          <p className="text-sm text-fg-muted">
            {run.steps.length === 0
              ? 'Scoring your resume against the posting…'
              : 'Working on the next step…'}
          </p>
        </li>
      )}
    </ol>
  )
}
