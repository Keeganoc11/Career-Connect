import { useEffect, useRef, useState, useSyncExternalStore } from 'react'

/**
 * The open overlays, oldest first.
 *
 * Every modal used to attach its own document-level Escape listener, so one
 * press closed the entire stack at once — open Email updates, open Add
 * application on top of it, press Escape, and both vanished. Here a single
 * listener reaches only the last entry.
 *
 * The stack also owns the things that must happen exactly once no matter how
 * many overlays are open: locking body scroll and making the app behind them
 * inert. Overlays portal to <body>, so `inert` on #root can't reach them —
 * and neither can it reach the toast region, which is mounted outside #root
 * for this reason.
 */
export interface OverlayHandle {
  close: () => void
  /** False while a save is in flight — dismissing then would strand the write. */
  dismissable: () => boolean
}

export type OverlayHandleRef = { current: OverlayHandle }

interface Entry {
  id: number
  handle: OverlayHandleRef
  /**
   * Whether the app behind this overlay should stop taking input. True for a
   * dialog; false for a popover or menu, which must leave the page clickable —
   * inerting it would kill click-outside-to-dismiss and freeze the very button
   * that opened the menu.
   */
  blocking: boolean
}

const stack: Entry[] = []
const listeners = new Set<() => void>()
let nextId = 1

let version = 0

function emit() {
  version++
  for (const listener of listeners) listener()
}

function handleKeyDown(event: KeyboardEvent) {
  if (event.key !== 'Escape' || stack.length === 0) return
  const { handle } = stack[stack.length - 1]
  if (!handle.current.dismissable()) return
  // Capture phase plus stopPropagation: nothing below should also react to the
  // same press, which is the bug this whole module exists to fix.
  event.stopPropagation()
  event.preventDefault()
  handle.current.close()
}

function setBackgroundBlocked(blocked: boolean) {
  const root = document.getElementById('root')
  if (root) {
    // setAttribute rather than the `inert` property: equivalent, and it doesn't
    // depend on which TS DOM lib version is in play.
    if (blocked) root.setAttribute('inert', '')
    else root.removeAttribute('inert')
  }
  // html has scrollbar-gutter: stable, so this doesn't shift the page.
  document.body.style.overflow = blocked ? 'hidden' : ''
}

/**
 * Derived from the whole stack rather than from its size: a menu opened over a
 * dialog must not un-block the page on its way out, and a menu opened over
 * nothing must not block it in the first place.
 */
function syncBackground() {
  setBackgroundBlocked(stack.some((entry) => entry.blocking))
}

export function pushOverlay(handle: OverlayHandleRef, blocking = true): number {
  const id = nextId++
  stack.push({ id, handle, blocking })
  if (stack.length === 1) {
    document.addEventListener('keydown', handleKeyDown, true)
  }
  syncBackground()
  emit()
  return id
}

export function removeOverlay(id: number) {
  const index = stack.findIndex((entry) => entry.id === id)
  if (index === -1) return
  stack.splice(index, 1)
  if (stack.length === 0) {
    document.removeEventListener('keydown', handleKeyDown, true)
  }
  syncBackground()
  emit()
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

function versionSnapshot(): number {
  return version
}

/**
 * Whether a *blocking* overlay sits above this one — the condition for going
 * inert.
 *
 * Deliberately not "is this the topmost": a menu opened inside a dialog sits
 * above it without blocking it, and inerting the dialog then would freeze it
 * and, because inert suppresses pointer events, stop a click on it from
 * dismissing that menu.
 */
export function useBlockedFromAbove(id: number | null): boolean {
  useSyncExternalStore(subscribe, versionSnapshot, versionSnapshot)
  if (id === null) return false
  const index = stack.findIndex((entry) => entry.id === id)
  if (index === -1) return false
  return stack.slice(index + 1).some((entry) => entry.blocking)
}

/**
 * Registers an overlay for as long as it's open, and returns its id.
 *
 * The callbacks are read through a ref that's refreshed every render, so the
 * stack always calls the current ones without the entry being torn down and
 * re-pushed — re-pushing would reorder the stack and make a background overlay
 * topmost mid-interaction.
 */
export function useOverlay(
  active: boolean,
  close: () => void,
  dismissable: () => boolean = () => true,
  /** False for popovers and menus — see Entry.blocking. */
  blocking = true,
): number | null {
  const handle = useRef<OverlayHandle>({ close, dismissable })
  handle.current = { close, dismissable }

  const [id, setId] = useState<number | null>(null)

  useEffect(() => {
    if (!active) return
    const entryId = pushOverlay(handle, blocking)
    setId(entryId)
    return () => {
      removeOverlay(entryId)
      setId(null)
    }
  }, [active, blocking])

  return id
}
