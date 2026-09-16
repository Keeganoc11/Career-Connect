import { STATUSES, type ApplicationStatus } from '../api/types'

/**
 * Sentence case, per the glossary — "Phone screen", never the wire value
 * "PhoneScreen".
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
 * One dot colour per status, drawn from the tokens. The palette stays clear of
 * the accent hue so "interactive" and "Interview" never look alike, and the
 * label is always beside it — colour is never the only signal.
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
