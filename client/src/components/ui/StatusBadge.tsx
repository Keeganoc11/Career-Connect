import type { ApplicationStatus } from '../../api/types'
import { STATUS_DOT, STATUS_LABELS } from '../../lib/status'
import { Badge } from './Badge'

/** A neutral chip with a colored dot — never a colored pill, which read as feedback. */
export function StatusBadge({ status }: { status: ApplicationStatus }) {
  return <Badge dotClass={STATUS_DOT[status]}>{STATUS_LABELS[status]}</Badge>
}
