import { useEffect, type RefObject } from 'react'

const FOCUSABLE = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled]):not([type="hidden"])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(', ')

function focusable(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>(FOCUSABLE)).filter(
    // getClientRects rather than offsetParent: offsetParent is null for any
    // position:fixed element, which would empty this list inside a portaled
    // popover. An unrendered element has no client rects either way.
    (el) => !el.closest('[inert]') && el.getClientRects().length > 0,
  )
}

/**
 * Keyboard containment for an overlay: focus moves in when it opens, Tab can't
 * leave it, and focus returns to whatever opened it on close.
 *
 * Nothing managed focus before this — opening a dialog left the keyboard
 * parked on the page behind it, so Tab walked the background instead.
 */
export function useFocusScope(
  ref: RefObject<HTMLElement | null>,
  active: boolean,
  /** Where focus should land on open. Defaults to the first focusable element. */
  initialFocus?: RefObject<HTMLElement | null>,
) {
  useEffect(() => {
    if (!active) return
    const container = ref.current
    if (!container) return

    // Captured before we move focus, so it can be handed back on close. The
    // opener is often a button that unmounts with the overlay, hence the guard
    // on restore below.
    const previous = document.activeElement as HTMLElement | null

    const target = initialFocus?.current ?? focusable(container)[0] ?? container
    if (target === container && !container.hasAttribute('tabindex')) {
      // An overlay with nothing focusable still needs to take focus, or the
      // screen reader keeps reading the page behind it.
      container.setAttribute('tabindex', '-1')
    }
    target.focus()

    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Tab') return
      const items = focusable(container)
      if (items.length === 0) {
        event.preventDefault()
        return
      }
      const first = items[0]
      const last = items[items.length - 1]
      const current = document.activeElement

      if (event.shiftKey && (current === first || !container.contains(current))) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && current === last) {
        event.preventDefault()
        first.focus()
      }
    }

    container.addEventListener('keydown', onKeyDown)
    return () => {
      container.removeEventListener('keydown', onKeyDown)
      if (previous && document.contains(previous)) previous.focus()
    }
  }, [ref, active, initialFocus])
}
