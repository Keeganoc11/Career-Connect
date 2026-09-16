import { useSyncExternalStore } from 'react'

export type ToastTone = 'success' | 'info' | 'error'

export interface Toast {
  id: number
  tone: ToastTone
  message: string
}

/** Beyond three, the stack covers the page it's reporting on. Oldest goes first. */
const MAX_VISIBLE = 3

let toasts: Toast[] = []
const listeners = new Set<() => void>()
let nextId = 1

function emit() {
  for (const listener of listeners) listener()
}

function add(tone: ToastTone, message: string) {
  toasts = [...toasts, { id: nextId++, tone, message }].slice(-MAX_VISIBLE)
  emit()
}

/**
 * Around fifteen actions used to succeed with no acknowledgement at all —
 * the save happened and the interface looked identical.
 *
 * Success and info dismiss themselves; errors stay until dismissed, since
 * something that failed shouldn't quietly disappear.
 */
export const toast = {
  success: (message: string) => add('success', message),
  info: (message: string) => add('info', message),
  error: (message: string) => add('error', message),
}

export function dismissToast(id: number) {
  toasts = toasts.filter((t) => t.id !== id)
  emit()
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

function snapshot() {
  return toasts
}

export function useToasts(): Toast[] {
  return useSyncExternalStore(subscribe, snapshot, snapshot)
}
