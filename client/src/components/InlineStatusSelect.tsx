import { useEffect, useRef, useState } from 'react'
import { STATUSES, type ApplicationStatus } from '../api/types'
import { STATUS_META } from '../lib/status'

interface Props {
  value: ApplicationStatus
  disabled?: boolean
  onChange: (status: ApplicationStatus) => void
}

/**
 * The day-to-day control: a status badge that opens a dropdown so status can be
 * changed straight from the list without opening the edit form.
 */
export function InlineStatusSelect({ value, disabled, onChange }: Props) {
  const [open, setOpen] = useState(false)
  const containerRef = useRef<HTMLDivElement>(null)
  const meta = STATUS_META[value]

  useEffect(() => {
    if (!open) return
    const close = (event: MouseEvent) => {
      if (!containerRef.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', close)
    return () => document.removeEventListener('mousedown', close)
  }, [open])

  return (
    <div ref={containerRef} className="relative inline-block">
      <button
        type="button"
        disabled={disabled}
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="listbox"
        aria-expanded={open}
        className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ring-1 ring-inset transition hover:brightness-95 disabled:opacity-50 ${meta.badge}`}
      >
        <span className={`size-1.5 rounded-full ${meta.dot}`} aria-hidden />
        {meta.label}
        <svg className="size-3 opacity-60" viewBox="0 0 12 12" fill="none" aria-hidden>
          <path d="M3 4.5 6 7.5 9 4.5" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
        </svg>
      </button>

      {open && (
        <ul
          role="listbox"
          className="absolute left-0 z-20 mt-1.5 w-40 overflow-hidden rounded-lg border border-slate-200 bg-white py-1 shadow-lg"
        >
          {STATUSES.map((status) => (
            <li key={status}>
              <button
                type="button"
                role="option"
                aria-selected={status === value}
                onClick={() => {
                  setOpen(false)
                  if (status !== value) onChange(status)
                }}
                className={`flex w-full items-center gap-2 px-3 py-1.5 text-left text-sm hover:bg-slate-50 ${
                  status === value ? 'font-semibold text-slate-900' : 'text-slate-600'
                }`}
              >
                <span className={`size-2 rounded-full ${STATUS_META[status].dot}`} aria-hidden />
                {STATUS_META[status].label}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
