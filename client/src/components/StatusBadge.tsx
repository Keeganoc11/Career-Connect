import { STATUS_META } from '../lib/status'
import type { ApplicationStatus } from '../api/types'

export function StatusBadge({ status }: { status: ApplicationStatus }) {
  const meta = STATUS_META[status]
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ring-1 ring-inset ${meta.badge}`}
    >
      <span className={`size-1.5 rounded-full ${meta.dot}`} aria-hidden />
      {meta.label}
    </span>
  )
}
