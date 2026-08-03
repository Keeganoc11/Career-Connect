import { STATUSES, type ApplicationStatus, type Summary } from '../api/types'
import { STATUS_META } from '../lib/status'

interface Props {
  summary: Summary
  activeFilter: ApplicationStatus | null
  onFilterChange: (status: ApplicationStatus | null) => void
}

export function SummaryBar({ summary, activeFilter, onFilterChange }: Props) {
  const countFor = (status: ApplicationStatus) =>
    summary.counts.find((c) => c.status === status)?.count ?? 0

  return (
    <section aria-label="Pipeline summary" className="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-8">
      <button
        type="button"
        onClick={() => onFilterChange(null)}
        className={`rounded-xl border-t-4 bg-white p-3 text-left shadow-sm ring-1 ring-slate-200 transition hover:shadow ${
          activeFilter === null ? 'border-slate-800' : 'border-transparent'
        }`}
      >
        <div className="text-2xl font-semibold tabular-nums text-slate-900">{summary.total}</div>
        <div className="mt-0.5 text-xs font-medium text-slate-500">Total</div>
      </button>

      {STATUSES.map((status) => {
        const meta = STATUS_META[status]
        const active = activeFilter === status
        return (
          <button
            key={status}
            type="button"
            onClick={() => onFilterChange(active ? null : status)}
            title={`Show only ${meta.label}`}
            className={`rounded-xl border-t-4 bg-white p-3 text-left shadow-sm ring-1 ring-slate-200 transition hover:shadow ${
              active ? meta.tileAccent : 'border-transparent'
            }`}
          >
            <div className="text-2xl font-semibold tabular-nums text-slate-900">
              {countFor(status)}
            </div>
            <div className="mt-0.5 flex items-center gap-1.5 text-xs font-medium text-slate-500">
              <span className={`size-1.5 rounded-full ${meta.dot}`} aria-hidden />
              {meta.label}
            </div>
          </button>
        )
      })}
    </section>
  )
}
