import type {
  InputHTMLAttributes,
  SelectHTMLAttributes,
  TextareaHTMLAttributes,
  ReactNode,
} from 'react'

/**
 * One control surface for every field. The focus ring comes from the global
 * :where() rule, so these don't restate it and can't drift from it.
 */
const CONTROL =
  'w-full rounded-control border border-line bg-surface px-3 py-2 text-sm text-fg placeholder:text-fg-subtle disabled:opacity-60 aria-[invalid]:border-danger'

export function Input(props: Omit<InputHTMLAttributes<HTMLInputElement>, 'className'>) {
  return <input {...props} className={CONTROL} />
}

export function Textarea({
  monospace = false,
  ...props
}: Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'className'> & { monospace?: boolean }) {
  return (
    <textarea
      {...props}
      className={`${CONTROL} ${monospace ? 'font-mono leading-relaxed' : ''}`}
    />
  )
}

export function Select(props: Omit<SelectHTMLAttributes<HTMLSelectElement>, 'className'>) {
  return <select {...props} className={CONTROL} />
}

interface CheckboxProps extends Omit<InputHTMLAttributes<HTMLInputElement>, 'className' | 'type'> {
  label: ReactNode
}

/** Label wraps the box, so the text is part of the target. */
export function Checkbox({ label, ...props }: CheckboxProps) {
  return (
    <label className="flex cursor-pointer items-start gap-2.5 text-sm text-fg">
      <input
        {...props}
        type="checkbox"
        className="mt-0.5 size-4 rounded-sm border-line text-accent accent-accent"
      />
      <span>{label}</span>
    </label>
  )
}
