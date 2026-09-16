import { useEffect, useRef, useState } from 'react'
import { AlertCircle, CheckCircle2, Info, X } from 'lucide-react'
import { dismissToast, useToasts, type Toast, type ToastTone } from './toast'

const DISMISS_AFTER = 4000

const ICONS: Record<ToastTone, typeof Info> = {
  success: CheckCircle2,
  info: Info,
  error: AlertCircle,
}

const TONES: Record<ToastTone, string> = {
  success: 'text-success',
  info: 'text-info',
  error: 'text-danger',
}

/**
 * Bottom-right, and above the mobile tab bar so it never sits under it.
 *
 * Mounted outside #root in main.tsx: the overlay stack marks #root inert while
 * a dialog is open, which would otherwise mute a toast the dialog just raised.
 *
 * Both live regions render at all times, empty or not. A region added to the
 * DOM at the same moment as its first message is inconsistently announced.
 */
export function Toaster() {
  const toasts = useToasts()
  const [paused, setPaused] = useState(false)

  const polite = toasts.filter((t) => t.tone !== 'error')
  const assertive = toasts.filter((t) => t.tone === 'error')

  return (
    <div className="pointer-events-none fixed inset-x-0 bottom-0 z-[60] flex flex-col items-end gap-2 p-4 pb-[calc(1rem+env(safe-area-inset-bottom))] sm:p-6">
      <div role="status" aria-live="polite" className="flex w-full flex-col items-end gap-2">
        {polite.map((t) => (
          <ToastRow key={t.id} toast={t} paused={paused} onHoverChange={setPaused} />
        ))}
      </div>
      <div role="alert" aria-live="assertive" className="flex w-full flex-col items-end gap-2">
        {assertive.map((t) => (
          <ToastRow key={t.id} toast={t} paused={paused} onHoverChange={setPaused} />
        ))}
      </div>
    </div>
  )
}

function ToastRow({
  toast,
  paused,
  onHoverChange,
}: {
  toast: Toast
  paused: boolean
  onHoverChange: (paused: boolean) => void
}) {
  const Icon = ICONS[toast.tone]
  const dismiss = useRef(() => dismissToast(toast.id))
  dismiss.current = () => dismissToast(toast.id)

  useEffect(() => {
    // Errors persist. Anything else pauses while the pointer or keyboard is on
    // the stack, so a toast can't vanish mid-read or mid-Tab.
    if (toast.tone === 'error' || paused) return
    const timer = setTimeout(() => dismiss.current(), DISMISS_AFTER)
    return () => clearTimeout(timer)
  }, [toast.tone, paused])

  return (
    <div
      onMouseEnter={() => onHoverChange(true)}
      onMouseLeave={() => onHoverChange(false)}
      onFocusCapture={() => onHoverChange(true)}
      onBlurCapture={() => onHoverChange(false)}
      className="pointer-events-auto flex w-full max-w-sm items-start gap-2.5 rounded-surface border border-line bg-surface px-3.5 py-3 shadow-overlay"
    >
      <Icon className={`mt-px size-4 shrink-0 ${TONES[toast.tone]}`} aria-hidden />
      <p className="flex-1 text-sm text-fg">{toast.message}</p>
      <button
        type="button"
        aria-label="Dismiss"
        onClick={() => dismissToast(toast.id)}
        className="-m-1 rounded-control p-1 text-fg-subtle transition-colors hover:bg-surface-muted hover:text-fg"
      >
        <X className="size-3.5" aria-hidden />
      </button>
    </div>
  )
}
