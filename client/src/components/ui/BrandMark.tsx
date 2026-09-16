/** The mark from favicon.svg, inline so it inherits crisp rendering at any size. */
export function BrandMark({ className = 'size-7' }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 32 32" aria-hidden>
      <rect width="32" height="32" rx="8" fill="var(--color-accent)" />
      <path
        d="M21.1 11.2a7 7 0 1 0 0 9.6"
        fill="none"
        stroke="var(--color-accent-fg)"
        strokeWidth="3"
        strokeLinecap="round"
      />
      <circle cx="24" cy="16" r="2" fill="var(--color-accent-fg)" />
    </svg>
  )
}
