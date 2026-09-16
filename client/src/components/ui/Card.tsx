import type { ReactNode } from 'react'

interface Props {
  children: ReactNode
  /** Lifts the card slightly. Flat surfaces get a border instead — no colored glows. */
  raised?: boolean
  padded?: boolean
}

export function Card({ children, raised = false, padded = true }: Props) {
  return (
    <section
      className={[
        'rounded-surface border border-line bg-surface',
        raised ? 'shadow-raised' : '',
        padded ? 'p-4' : '',
      ]
        .filter(Boolean)
        .join(' ')}
    >
      {children}
    </section>
  )
}
