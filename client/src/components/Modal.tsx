import type { ReactNode } from 'react'
import { useEscapeKey } from '../lib/useEscapeKey'

interface BackdropProps {
  onClose: () => void
  children: ReactNode
}

/** Full-screen backdrop shared by every large modal: click-outside and Escape both close it. */
export function ModalBackdrop({ onClose, children }: BackdropProps) {
  useEscapeKey(onClose)
  return (
    <div
      className="fixed inset-0 z-30 flex items-start justify-center overflow-y-auto bg-slate-900/50 p-4 pt-10 backdrop-blur-sm"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      {children}
    </div>
  )
}

interface HeaderProps {
  title: ReactNode
  subtitle?: ReactNode
  onClose: () => void
}

/** The brand-gradient title bar shared by every large modal. */
export function ModalHeader({ title, subtitle, onClose }: HeaderProps) {
  return (
    <div className="brand-gradient flex items-start justify-between gap-4 px-7 py-5">
      <div>
        <h2 className="text-xl font-bold text-white">{title}</h2>
        {subtitle && <p className="mt-0.5 text-sm text-white/80">{subtitle}</p>}
      </div>
      <button
        type="button"
        onClick={onClose}
        aria-label="Close"
        className="rounded-lg p-1.5 text-white/80 transition hover:bg-white/20 hover:text-white"
      >
        ✕
      </button>
    </div>
  )
}
