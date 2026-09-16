import type { ButtonHTMLAttributes, ReactNode, Ref } from 'react'

interface Props extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'className' | 'children'> {
  /** So a Popover or Menu can anchor itself to this button. */
  ref?: Ref<HTMLButtonElement>
  /**
   * Required, and deliberately so. The unlabelled ✕ that silently disconnected
   * Gmail is the reason this primitive exists: an icon with no accessible name
   * is a button nobody can identify, by screen reader or by eye.
   */
  label: string
  icon: ReactNode
  /** Small count sitting on the icon — unread email updates, say. Hidden when 0. */
  count?: number
  tone?: 'default' | 'danger'
}

export function IconButton({ ref, label, icon, count, tone = 'default', ...rest }: Props) {
  const tones =
    tone === 'danger'
      ? 'text-fg-muted hover:bg-danger-soft hover:text-danger'
      : 'text-fg-muted hover:bg-surface-muted hover:text-fg'

  return (
    <button
      {...rest}
      ref={ref}
      type="button"
      aria-label={label}
      title={label}
      className={`relative inline-flex size-9 items-center justify-center rounded-control transition-colors disabled:pointer-events-none disabled:opacity-60 pointer-coarse:size-11 ${tones}`}
    >
      {icon}
      {count !== undefined && count > 0 && (
        <span
          className="absolute -right-0.5 -top-0.5 min-w-4 rounded-full bg-accent px-1 text-[0.625rem] font-semibold leading-4 text-accent-fg tabular-nums"
          aria-hidden
        >
          {count > 9 ? '9+' : count}
        </span>
      )}
    </button>
  )
}
