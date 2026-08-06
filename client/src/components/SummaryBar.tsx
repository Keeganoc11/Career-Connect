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
    <section aria-label="Pipeline summary">
      {/* Eight across only once there's genuinely room; four otherwise, so the
          longer labels ("Phone Screen") never truncate. */}
      <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 xl:grid-cols-8">
        {/* Total gets the brand treatment — it's the headline number. */}
        <button
          type="button"
          onClick={() => onFilterChange(null)}
          aria-pressed={activeFilter === null}
          className={`brand-gradient relative overflow-hidden rounded-2xl p-4 text-left text-white shadow-lg shadow-brand-600/25 transition ${
            activeFilter === null ? 'ring-4 ring-brand-500/30' : 'opacity-95 hover:opacity-100'
          }`}
        >
          <div className="text-4xl font-bold tabular-nums">{summary.total}</div>
          <div className="mt-1 text-sm font-semibold text-white/85">Total</div>
        </button>

        {STATUSES.map((status) => {
          const meta = STATUS_META[status]
          const count = countFor(status)
          const active = activeFilter === status
          return (
            <button
              key={status}
              type="button"
              onClick={() => onFilterChange(active ? null : status)}
              aria-pressed={active}
              title={`Show only ${meta.label}`}
              className={`rounded-2xl border-2 bg-white p-4 text-left shadow-sm transition hover:-translate-y-0.5 hover:shadow-md ${
                active ? meta.tileAccent : 'border-slate-200/70'
              }`}
            >
              <div
                className={`text-4xl font-bold tabular-nums ${
                  count > 0 ? 'text-slate-900' : 'text-slate-300'
                }`}
              >
                {count}
              </div>
              <div className="mt-1 flex items-start gap-1.5 text-sm font-semibold leading-tight text-slate-600">
                <span className={`mt-1.5 size-2 shrink-0 rounded-full ${meta.dot}`} aria-hidden />
                <span>{meta.label}</span>
              </div>
            </button>
          )
        })}
      </div>

      {activeFilter && (
        <p className="mt-3 text-sm text-slate-500">
          Filtered to <strong className="text-slate-700">{STATUS_META[activeFilter].label}</strong>.{' '}
          <button
            type="button"
            onClick={() => onFilterChange(null)}
            className="font-semibold text-brand-600 hover:underline"
          >
            Show all
          </button>
        </p>
      )}
    </section>
  )
}
