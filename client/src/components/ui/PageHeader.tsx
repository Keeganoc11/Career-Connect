import type { ReactNode } from 'react'

interface Props {
  title: string
  /** One line of orientation, or a count. */
  description?: ReactNode
  /** The page's primary action, top right. */
  actions?: ReactNode
}

export function PageHeader({ title, description, actions }: Props) {
  return (
    <div className="mb-5 flex flex-wrap items-start justify-between gap-3">
      <div>
        <h1 className="text-xl font-semibold text-fg">{title}</h1>
        {description && <p className="mt-0.5 text-sm text-fg-muted">{description}</p>}
      </div>
      {actions && <div className="flex items-center gap-2">{actions}</div>}
    </div>
  )
}
