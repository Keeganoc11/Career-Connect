import { useRef, type KeyboardEvent, type ReactNode, type RefObject } from 'react'
import { Check } from 'lucide-react'
import { Popover } from './Popover'

export interface MenuItem {
  key: string
  label: string
  icon?: ReactNode
  /** Renders as menuitemradio and shows a check when selected. */
  selected?: boolean
  destructive?: boolean
  disabled?: boolean
  onSelect: () => void
}

interface Props {
  anchorRef: RefObject<HTMLElement | null>
  items: MenuItem[]
  /** Names the menu for screen readers — "Actions for Acme". */
  label: string
  align?: 'start' | 'end'
  onClose: () => void
}

/**
 * A keyboard-navigable menu: arrows move, Home/End jump, and typing a letter
 * jumps to the next item starting with it. Replaces row actions that only
 * appeared on hover, which put them out of reach of the keyboard entirely.
 *
 * A separator is drawn before any destructive item, so Delete is never
 * adjacent to a routine action.
 */
export function Menu({ anchorRef, items, label, align = 'end', onClose }: Props) {
  const listRef = useRef<HTMLDivElement>(null)
  const typed = useRef({ query: '', at: 0 })

  const enabled = () =>
    Array.from(listRef.current?.querySelectorAll<HTMLButtonElement>('[role^="menuitem"]') ?? [])
      .filter((el) => !el.disabled)

  const focusAt = (index: number) => {
    const all = enabled()
    if (all.length === 0) return
    const wrapped = (index + all.length) % all.length
    all[wrapped].focus()
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const all = enabled()
    const current = all.indexOf(document.activeElement as HTMLButtonElement)

    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault()
        focusAt(current + 1)
        return
      case 'ArrowUp':
        event.preventDefault()
        focusAt(current - 1)
        return
      case 'Home':
        event.preventDefault()
        focusAt(0)
        return
      case 'End':
        event.preventDefault()
        focusAt(all.length - 1)
        return
    }

    if (event.key.length !== 1 || event.metaKey || event.ctrlKey || event.altKey) return

    // Type-ahead: successive letters within a second build up a prefix.
    const now = Date.now()
    typed.current.query = now - typed.current.at < 1000 ? typed.current.query + event.key : event.key
    typed.current.at = now

    const query = typed.current.query.toLowerCase()
    const match = all.findIndex((el) => el.textContent?.trim().toLowerCase().startsWith(query))
    if (match !== -1) {
      event.preventDefault()
      all[match].focus()
    }
  }

  return (
    <Popover anchorRef={anchorRef} align={align} onClose={onClose}>
      <div ref={listRef} role="menu" aria-label={label} onKeyDown={onKeyDown}>
        {items.map((item, index) => (
          <div key={item.key}>
            {item.destructive && index > 0 && !items[index - 1].destructive && (
              <div className="my-1 border-t border-line" role="separator" />
            )}
            <button
              type="button"
              role={item.selected === undefined ? 'menuitem' : 'menuitemradio'}
              aria-checked={item.selected}
              disabled={item.disabled}
              onClick={() => {
                item.onSelect()
                onClose()
              }}
              className={`flex w-full items-center gap-2 px-3 py-2 text-left text-sm transition-colors disabled:pointer-events-none disabled:opacity-60 pointer-coarse:py-2.5 ${
                item.destructive
                  ? 'text-danger hover:bg-danger-soft'
                  : 'text-fg hover:bg-surface-muted'
              }`}
            >
              {item.icon}
              <span className="flex-1">{item.label}</span>
              {item.selected && <Check className="size-4 text-accent" aria-hidden />}
            </button>
          </div>
        ))}
      </div>
    </Popover>
  )
}
