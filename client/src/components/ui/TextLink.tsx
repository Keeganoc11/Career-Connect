import type { AnchorHTMLAttributes, ReactNode } from 'react'

interface Props extends Omit<AnchorHTMLAttributes<HTMLAnchorElement>, 'className'> {
  children: ReactNode
  /** Adds the rel guard that any target="_blank" link needs. */
  external?: boolean
}

export function TextLink({ children, external, ...rest }: Props) {
  return (
    <a
      {...rest}
      {...(external ? { target: '_blank', rel: 'noreferrer noopener' } : null)}
      className="rounded-sm font-medium text-accent underline underline-offset-2 hover:text-accent-hover"
    >
      {children}
    </a>
  )
}
