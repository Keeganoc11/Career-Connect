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
  Preparing: {
    label: 'Preparing',
    badge: 'bg-amber-50 text-amber-800 ring-amber-600/20',
    dot: 'bg-amber-500',
    tileAccent: 'border-amber-500',
  },
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
    badge: 'bg-stone-100 text-stone-700 ring-stone-500/20',
    dot: 'bg-stone-400',
    tileAccent: 'border-stone-400',
  },
}

/**
 * Sentence case, per the glossary. STATUS_META.label above is title case
 * ("Phone Screen") and still drives every screen that hasn't moved to the new
 * primitives yet; R6 retires it, and until then the two coexist on purpose.
 */
export const STATUS_LABELS: Record<ApplicationStatus, string> = {
  Preparing: 'Preparing',
  Applied: 'Applied',
  PhoneScreen: 'Phone screen',
  Interview: 'Interview',
  Offer: 'Offer',
  Rejected: 'Rejected',
  Ghosted: 'Ghosted',
  Withdrawn: 'Withdrawn',
}

/**
 * One dot color per status, drawn from the tokens. The palette stays clear of
 * the accent hue so "interactive" and "Interview" never look alike, and the
 * label is always beside it — color is never the only signal.
 */
export const STATUS_DOT: Record<ApplicationStatus, string> = {
  Preparing: 'bg-status-preparing',
  Applied: 'bg-status-applied',
  PhoneScreen: 'bg-status-phone-screen',
  Interview: 'bg-status-interview',
  Offer: 'bg-status-offer',
  Rejected: 'bg-status-rejected',
  Ghosted: 'bg-status-ghosted',
  Withdrawn: 'bg-status-withdrawn',
}

/** Pipeline position used when sorting by status. */
export const STATUS_ORDER: Record<ApplicationStatus, number> = Object.fromEntries(
  STATUSES.map((status, index) => [status, index]),
) as Record<ApplicationStatus, number>
