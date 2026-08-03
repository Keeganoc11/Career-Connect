import { STATUSES, type ApplicationStatus } from '../api/types'

interface StatusMeta {
  label: string
  /** Badge pill (background, text, ring). */
  badge: string
  /** Solid dot shown inside badges and summary tiles. */
  dot: string
  /** Accent border for the summary tile when active. */
  tileAccent: string
}

export const STATUS_META: Record<ApplicationStatus, StatusMeta> = {
  Applied: {
    label: 'Applied',
    badge: 'bg-blue-50 text-blue-800 ring-blue-600/20',
    dot: 'bg-blue-500',
    tileAccent: 'border-blue-500',
  },
  PhoneScreen: {
    label: 'Phone Screen',
    badge: 'bg-cyan-50 text-cyan-800 ring-cyan-600/20',
    dot: 'bg-cyan-500',
    tileAccent: 'border-cyan-500',
  },
  Interview: {
    label: 'Interview',
    badge: 'bg-violet-50 text-violet-800 ring-violet-600/20',
    dot: 'bg-violet-500',
    tileAccent: 'border-violet-500',
  },
  Offer: {
    label: 'Offer',
    badge: 'bg-emerald-50 text-emerald-800 ring-emerald-600/20',
    dot: 'bg-emerald-500',
    tileAccent: 'border-emerald-500',
  },
  Rejected: {
    label: 'Rejected',
    badge: 'bg-rose-50 text-rose-800 ring-rose-600/20',
    dot: 'bg-rose-500',
    tileAccent: 'border-rose-500',
  },
  Ghosted: {
    label: 'Ghosted',
    badge: 'bg-slate-100 text-slate-600 ring-slate-500/20',
    dot: 'bg-slate-400',
    tileAccent: 'border-slate-400',
  },
  Withdrawn: {
    label: 'Withdrawn',
    badge: 'bg-amber-50 text-amber-800 ring-amber-600/20',
    dot: 'bg-amber-500',
    tileAccent: 'border-amber-500',
  },
}

/** Pipeline position used when sorting by status. */
export const STATUS_ORDER: Record<ApplicationStatus, number> = Object.fromEntries(
  STATUSES.map((status, index) => [status, index]),
) as Record<ApplicationStatus, number>
