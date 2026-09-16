import type { ReactNode } from 'react'

interface Props {
  children: ReactNode
  /** A dot before the label, for categories that need a color cue as well as text. */
  dotClass?: string
  /** Draws attention without claiming a status — "Soon", "Active". */
  emphasis?: 'neutral' | 'accent' | 'interview'
}

const EMPHASIS = {
  neutral: 'bg-surface-muted text-fg-muted ring-line',
  accent: 'bg-accent-soft text-accent ring-accent/20',
  interview: 'bg-status-interview-soft text-status-interview ring-status-interview/20',
}

/**
 * A quiet chip. Feedback (success, error) deliberately never renders as one —
 * it goes to a Banner or a toast instead, so "Saved" can't be mistaken at a
 * glance for a pipeline status like "Offer".
 */
export function Badge({ children, dotClass, emphasis = 'neutral' }: Props) {
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset ${EMPHASIS[emphasis]}`}
    >
      {dotClass && <span className={`size-1.5 rounded-full ${dotClass}`} aria-hidden />}
      {children}
    </span>
  )
}
