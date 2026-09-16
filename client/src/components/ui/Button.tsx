import type { ButtonHTMLAttributes, ReactNode, Ref } from 'react'
import { Spinner } from './Spinner'

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger'
export type ButtonSize = 'sm' | 'md'

const VARIANTS: Record<ButtonVariant, string> = {
  primary: 'bg-accent text-accent-fg hover:bg-accent-hover',
  secondary: 'border border-line bg-surface text-fg hover:bg-surface-muted',
  ghost: 'text-fg-muted hover:bg-surface-muted hover:text-fg',
  // Reserved for the confirming button of a destructive dialog. The entry point
  // that opens that dialog uses tone="danger" instead — it isn't destructive yet.
  danger: 'bg-danger text-white hover:bg-danger/90',
}

const SIZES: Record<ButtonSize, string> = {
  // pointer-coarse grows the target on touch without inflating it on desktop.
  sm: 'h-8 gap-1.5 px-3 pointer-coarse:h-10',
  md: 'h-9 gap-2 px-4 pointer-coarse:h-11',
}

interface Props extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'className'> {
  /** So a dialog can put initial focus on Cancel rather than on its destructive button. */
  ref?: Ref<HTMLButtonElement>
  variant?: ButtonVariant
  size?: ButtonSize
  /** Colors a secondary/ghost button as destructive — for entry points that open a confirm. */
  tone?: 'danger'
  /** Swaps in a spinner and blocks repeat presses. */
  loading?: boolean
  /** Leading icon. Decorative, so pass a lucide icon and let the label speak. */
  icon?: ReactNode
  fullWidth?: boolean
  children: ReactNode
}

/**
 * The one button in the app. Before this there were three fill styles across
 * eleven size combinations, four different Cancel buttons, and a Retry styled
 * like a delete.
 *
 * The focus ring comes from the global :where() rule in index.css, which sits
 * at zero specificity — so a button can still override it if it ever needs to.
 */
export function Button({
  ref,
  variant = 'secondary',
  size = 'md',
  tone,
  loading = false,
  icon,
  fullWidth = false,
  disabled,
  children,
  type = 'button',
  ...rest
}: Props) {
  const toned =
    tone === 'danger' && variant !== 'danger' && variant !== 'primary'
      ? 'text-danger hover:bg-danger-soft hover:text-danger'
      : ''

  return (
    <button
      {...rest}
      ref={ref}
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={[
        'inline-flex items-center justify-center rounded-control text-sm font-medium',
        'transition-colors disabled:pointer-events-none disabled:opacity-60',
        SIZES[size],
        VARIANTS[variant],
        toned,
        fullWidth ? 'w-full' : '',
      ]
        .filter(Boolean)
        .join(' ')}
    >
      {loading ? <Spinner /> : icon}
      {children}
    </button>
  )
}
