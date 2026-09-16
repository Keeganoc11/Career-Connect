import { useRef, useState } from 'react'
import { ChevronDown } from 'lucide-react'
import { STATUSES, type ApplicationStatus } from '../api/types'
import { STATUS_DOT, STATUS_LABELS } from '../lib/status'
import { Menu } from './ui'

interface Props {
  value: ApplicationStatus
  disabled?: boolean
  onChange: (status: ApplicationStatus) => void
}

/**
 * Change status straight from the list.
 *
 * Built on the portaled Menu: the dropdown it replaces was absolutely
 * positioned inside the table, so on the bottom rows it was clipped by the
 * table's own overflow and the lower options simply couldn't be reached.
 */
export function StatusMenu({ value, disabled, onChange }: Props) {
  const buttonRef = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        disabled={disabled}
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={open}
        className="inline-flex items-center gap-1.5 rounded-full border border-line px-2.5 py-1 text-xs font-medium text-fg transition-colors hover:bg-surface-muted disabled:pointer-events-none disabled:opacity-60 pointer-coarse:py-2"
      >
        <span className={`size-1.5 rounded-full ${STATUS_DOT[value]}`} aria-hidden />
        {STATUS_LABELS[value]}
        <ChevronDown className="size-3.5 text-fg-subtle" aria-hidden />
      </button>

      {open && (
        <Menu
          anchorRef={buttonRef}
          align="start"
          label="Change status"
          onClose={() => setOpen(false)}
          items={STATUSES.map((status) => ({
            key: status,
            label: STATUS_LABELS[status],
            selected: status === value,
            icon: <span className={`size-1.5 rounded-full ${STATUS_DOT[status]}`} aria-hidden />,
            onSelect: () => {
              if (status !== value) onChange(status)
            },
          }))}
        />
      )}
    </>
  )
}
