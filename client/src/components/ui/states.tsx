import type { ReactNode } from 'react'
import { Spinner } from './Spinner'

/**
 * One loading treatment and one empty treatment. There were four of each, so
 * the same waiting state looked different depending on which page you were on.
 */
export function LoadingState({ label = 'Loading…' }: { label?: string }) {
  return (
    <div className="flex items-center justify-center gap-2.5 py-12 text-sm text-fg-muted">
      <Spinner />
      <span>{label}</span>
    </div>
  )
}

interface EmptyProps {
  title: string
  /** One line on what to do about it — never "on the right", which isn't true on a phone. */
  description?: string
  icon?: ReactNode
  action?: ReactNode
}

export function EmptyState({ title, description, icon, action }: EmptyProps) {
  return (
    <div className="flex flex-col items-center px-6 py-12 text-center">
      {icon && <div className="mb-3 text-fg-subtle">{icon}</div>}
      <p className="text-sm font-medium text-fg">{title}</p>
      {description && <p className="mt-1 max-w-sm text-sm text-fg-muted">{description}</p>}
      {action && <div className="mt-4">{action}</div>}
    </div>
  )
}
