import { useEffect, useLayoutEffect, useRef, useState, type ReactNode, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import { useOverlay } from './overlayStack'
import { useFocusScope } from './useFocusScope'

const GUTTER = 8

interface Props {
  anchorRef: RefObject<HTMLElement | null>
  children: ReactNode
  /** Aligns the panel's right edge with the anchor's — for menus near the right edge. */
  align?: 'start' | 'end'
  onClose: () => void
}

/**
 * A panel anchored to a trigger, portaled to <body> and positioned with fixed
 * coordinates.
 *
 * The status dropdown it replaces was absolutely positioned inside the table,
 * so on the bottom rows it was clipped by the table's own overflow and the
 * options simply couldn't be reached.
 */
export function Popover({ anchorRef, children, align = 'start', onClose }: Props) {
  const panelRef = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState<{ top: number; left: number } | null>(null)

  useOverlay(true, onClose)
  useFocusScope(panelRef, true)

  useLayoutEffect(() => {
    const anchor = anchorRef.current
    const panel = panelRef.current
    if (!anchor || !panel) return

    const place = () => {
      const a = anchor.getBoundingClientRect()
      const p = panel.getBoundingClientRect()

      // Below the anchor by default; above when there isn't room and there is
      // room up top, so a trigger near the bottom still opens somewhere visible.
      const below = a.bottom + 4
      const above = a.top - p.height - 4
      const top =
        below + p.height <= window.innerHeight - GUTTER || above < GUTTER ? below : above

      const preferred = align === 'end' ? a.right - p.width : a.left
      const left = Math.min(
        Math.max(GUTTER, preferred),
        window.innerWidth - p.width - GUTTER,
      )

      setPosition({ top, left })
    }

    place()
    window.addEventListener('resize', place)
    return () => window.removeEventListener('resize', place)
  }, [anchorRef, align])

  useEffect(() => {
    const onPointerDown = (event: PointerEvent) => {
      const target = event.target as Node
      if (panelRef.current?.contains(target) || anchorRef.current?.contains(target)) return
      onClose()
    }
    // Scrolling anywhere outside the panel moves the anchor out from under it,
    // so close rather than chase it.
    const onScroll = (event: Event) => {
      if (panelRef.current?.contains(event.target as Node)) return
      onClose()
    }

    document.addEventListener('pointerdown', onPointerDown, true)
    document.addEventListener('scroll', onScroll, true)
    return () => {
      document.removeEventListener('pointerdown', onPointerDown, true)
      document.removeEventListener('scroll', onScroll, true)
    }
  }, [anchorRef, onClose])

  return createPortal(
    <div
      ref={panelRef}
      style={{ top: position?.top ?? -9999, left: position?.left ?? -9999 }}
      // Hidden until measured, so it never flashes at the wrong spot.
      className={`fixed z-50 min-w-48 rounded-surface border border-line bg-surface py-1 shadow-overlay ${
        position ? '' : 'invisible'
      }`}
    >
      {children}
    </div>,
    document.body,
  )
}
