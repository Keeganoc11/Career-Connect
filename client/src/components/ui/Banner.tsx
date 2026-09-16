import type { ReactNode } from 'react'

export type BannerTone = 'danger' | 'warning' | 'info' | 'success'

const TONES: Record<BannerTone, string> = {
  danger: 'border-danger/20 bg-danger-soft text-danger',
  warning: 'border-warning/20 bg-warning-soft text-warning',
  info: 'border-info/20 bg-info-soft text-info',
  success: 'border-success/20 bg-success-soft text-success',
}

interface Props {
  tone?: BannerTone
  children: ReactNode
  /** A single recovery action — "Try again". Never more than one. */
  action?: ReactNode
}

/**
 * An inline message anchored to the thing that failed, rather than a page-level
 * error setter that could report a Gmail failure as "Couldn't load your
 * applications."
 *
 * A success *banner* is almost always wrong — success belongs in a toast, or is
 * already visible in the result. The tone exists for the rare standing case.
 */
export function Banner({ tone = 'danger', children, action }: Props) {
  return (
    <div
      role={tone === 'danger' ? 'alert' : 'status'}
      className={`flex flex-wrap items-center justify-between gap-3 rounded-control border px-3 py-2.5 text-sm ${TONES[tone]}`}
    >
      <span>{children}</span>
      {action}
    </div>
  )
}
