import { STATUSES, type Application, type ApplicationStatus } from '../api/types'
import { STATUS_DOT, STATUS_LABELS } from '../lib/status'

interface Props {
  applications: Application[]
  value: ApplicationStatus | null
  onChange: (status: ApplicationStatus | null) => void
}

/**
 * One scrollable row of chips, replacing nine large count tiles that took
 * roughly 450px on desktop before a single application was visible.
 *
 * Counts come from the loaded list rather than a separate summary endpoint, so
 * they can't disagree with the rows underneath and there's no second request
 * popping the layout after the page has already drawn.
 */
export function StatusFilter({ applications, value, onChange }: Props) {
  const countFor = (status: ApplicationStatus) =>
    applications.reduce((total, a) => (a.status === status ? total + 1 : total), 0)

  const chip = (active: boolean) =>
    `inline-flex shrink-0 items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-medium transition-colors pointer-coarse:py-2 ${
      active
        ? 'border-accent bg-accent-soft text-accent'
        : 'border-line text-fg-muted hover:bg-surface-muted hover:text-fg'
    }`

  return (
    <div
      // Scrolls in its own row on a phone rather than wrapping to four lines.
      className="-mx-4 flex gap-2 overflow-x-auto px-4 pb-1 sm:mx-0 sm:flex-wrap sm:px-0"
    >
      <button
        type="button"
        onClick={() => onChange(null)}
        aria-pressed={value === null}
        className={chip(value === null)}
      >
        All <span className="tabular-nums">{applications.length}</span>
      </button>

      {STATUSES.map((status) => {
        const count = countFor(status)
        const active = value === status
        return (
          <button
            key={status}
            type="button"
            onClick={() => onChange(active ? null : status)}
            aria-pressed={active}
            className={chip(active)}
          >
            <span className={`size-1.5 rounded-full ${STATUS_DOT[status]}`} aria-hidden />
            {STATUS_LABELS[status]} <span className="tabular-nums">{count}</span>
          </button>
        )
      })}
    </div>
  )
}
