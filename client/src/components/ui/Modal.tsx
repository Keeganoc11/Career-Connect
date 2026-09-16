import { useId, useRef, useState, type ReactNode, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'
import { useIsTopmost, useOverlay } from './overlayStack'
import { useFocusScope } from './useFocusScope'
import { Banner } from './Banner'
import { Button } from './Button'
import { IconButton } from './IconButton'

export type ModalSize = 'sm' | 'md' | 'lg'

const SIZES: Record<ModalSize, string> = {
  sm: 'max-w-md',
  // md and lg go full-screen on a phone: a capped panel at 375px left slivers
  // of backdrop that looked like a mistake and stole room from the content.
  md: 'max-w-2xl max-sm:h-full max-sm:max-w-none max-sm:rounded-none',
  lg: 'max-w-4xl max-sm:h-full max-sm:max-w-none max-sm:rounded-none',
}

interface Props {
  /** Sentence-case noun phrase. No emoji, and never the company name — that goes in `description`. */
  title: string
  /** Usually "Company · Role". */
  description?: ReactNode
  size?: ModalSize
  children: ReactNode
  /** Primary action rightmost. */
  footer?: ReactNode
  /** Destructive or tertiary actions, pinned to the left of the footer. */
  footerStart?: ReactNode
  /** Shown just above the footer, next to the buttons that caused it. */
  error?: string | null
  /** Blocks dismissal while a save is in flight, so the write can't be stranded. */
  busy?: boolean
  /** Asks before discarding unsaved input. */
  dirty?: boolean
  initialFocus?: RefObject<HTMLElement | null>
  onClose: () => void
}

/**
 * Every dialog in the app. It replaces ModalBackdrop/ModalHeader and six
 * copy-pasted panels with three widths and three scroll heights between them.
 *
 * Dismissal is routed through one path — Escape, the backdrop, and the close
 * button all call requestClose — so the dirty guard and the busy lock can't be
 * bypassed by picking a different way out.
 */
export function Modal({
  title,
  description,
  size = 'md',
  children,
  footer,
  footerStart,
  error,
  busy = false,
  dirty = false,
  initialFocus,
  onClose,
}: Props) {
  const panelRef = useRef<HTMLDivElement>(null)
  const discardRef = useRef<HTMLButtonElement>(null)
  const [askingDiscard, setAskingDiscard] = useState(false)
  const titleId = useId()
  const descriptionId = useId()

  const requestClose = () => {
    if (busy) return
    if (dirty) {
      setAskingDiscard(true)
      return
    }
    onClose()
  }

  const id = useOverlay(true, requestClose, () => !busy && !askingDiscard)
  const topmost = useIsTopmost(id)
  useFocusScope(panelRef, !askingDiscard, initialFocus)

  return createPortal(
    <div
      className="fixed inset-0 z-40 flex items-center justify-center bg-fg/40 p-4 max-sm:p-0"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) requestClose()
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        // A modal that isn't on top stops taking clicks and Tab, matching the
        // page behind it.
        inert={!topmost}
        className={`flex max-h-[calc(100dvh-2rem)] w-full flex-col rounded-surface bg-surface shadow-overlay max-sm:max-h-dvh ${SIZES[size]}`}
      >
        <div className="flex items-start justify-between gap-4 border-b border-line px-5 py-4">
          <div className="min-w-0">
            <h2 id={titleId} className="text-base font-semibold text-fg">
              {title}
            </h2>
            {description && (
              <p id={descriptionId} className="mt-0.5 truncate text-xs text-fg-muted">
                {description}
              </p>
            )}
          </div>
          <IconButton
            label="Close"
            icon={<X className="size-4" aria-hidden />}
            onClick={requestClose}
            disabled={busy}
          />
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto px-5 py-4">{children}</div>

        {(error || footer || footerStart) && (
          <div className="border-t border-line px-5 py-4">
            {error && (
              <div className="mb-3">
                <Banner>{error}</Banner>
              </div>
            )}
            {(footer || footerStart) && (
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div className="flex items-center gap-2">{footerStart}</div>
                <div className="flex items-center gap-2 max-sm:w-full max-sm:justify-end">
                  {footer}
                </div>
              </div>
            )}
          </div>
        )}
      </div>

      {askingDiscard && (
        <DiscardPrompt
          initialFocus={discardRef}
          onKeepEditing={() => setAskingDiscard(false)}
          onDiscard={() => {
            setAskingDiscard(false)
            onClose()
          }}
        />
      )}
    </div>,
    document.body,
  )
}

/**
 * Stacked above its own modal rather than replacing it, so Escape reaches the
 * prompt first and the work underneath is still visible behind it. Kept local
 * to avoid a cycle: ConfirmDialog is built on Modal, so Modal can't use it.
 */
function DiscardPrompt({
  initialFocus,
  onKeepEditing,
  onDiscard,
}: {
  initialFocus: RefObject<HTMLButtonElement | null>
  onKeepEditing: () => void
  onDiscard: () => void
}) {
  const ref = useRef<HTMLDivElement>(null)
  const titleId = useId()
  useOverlay(true, onKeepEditing)
  useFocusScope(ref, true, initialFocus)

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-fg/40 p-4"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onKeepEditing()
      }}
    >
      <div
        ref={ref}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="w-full max-w-sm rounded-surface bg-surface p-5 shadow-overlay"
      >
        <h2 id={titleId} className="text-base font-semibold text-fg">
          Discard changes?
        </h2>
        <p className="mt-1.5 text-sm text-fg-muted">
          Your edits haven’t been saved and will be lost.
        </p>
        <div className="mt-5 flex justify-end gap-2">
          <Button ref={initialFocus} onClick={onKeepEditing}>
            Keep editing
          </Button>
          <Button variant="danger" onClick={onDiscard}>
            Discard
          </Button>
        </div>
      </div>
    </div>
  )
}
