import { useId, type ReactNode } from 'react'

/** The wiring a control needs to be described by its label, hint and error. */
export interface FieldControlProps {
  id: string
  'aria-describedby': string | undefined
  'aria-invalid': boolean | undefined
}

interface Props {
  label: string
  /** Steady guidance, shown under the control. */
  hint?: ReactNode
  /** Replaces the hint while present, and marks the control invalid. */
  error?: string | null
  /** Hides the label visually but keeps it for screen readers — for a search box with an icon. */
  labelHidden?: boolean
  children: (props: FieldControlProps) => ReactNode
}

/**
 * A render prop rather than a wrapper, so the ids actually reach the control.
 * The previous inputs were styled by a shared class string with no label
 * association at all — four of them bypassed even that.
 */
export function Field({ label, hint, error, labelHidden = false, children }: Props) {
  const id = useId()
  const describedById = `${id}-description`
  const described = error ?? hint

  return (
    <div>
      <label
        htmlFor={id}
        className={
          labelHidden
            ? 'sr-only'
            : 'mb-1.5 block text-sm font-medium text-fg'
        }
      >
        {label}
      </label>
      {children({
        id,
        'aria-describedby': described ? describedById : undefined,
        'aria-invalid': error ? true : undefined,
      })}
      {described && (
        <p
          id={describedById}
          className={`mt-1.5 text-xs ${error ? 'text-danger' : 'text-fg-muted'}`}
        >
          {described}
        </p>
      )}
    </div>
  )
}
