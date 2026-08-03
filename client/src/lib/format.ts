const dateFormatter = new Intl.DateTimeFormat('en-US', {
  month: 'short',
  day: 'numeric',
  year: 'numeric',
})

/** "2026-08-01" → "Aug 1, 2026" without timezone drift. */
export function formatDate(isoDate: string): string {
  const [year, month, day] = isoDate.split('-').map(Number)
  return dateFormatter.format(new Date(year, month - 1, day))
}

export function formatRelative(isoUtc: string): string {
  const then = new Date(isoUtc.endsWith('Z') ? isoUtc : `${isoUtc}Z`)
  const seconds = Math.max(0, (Date.now() - then.getTime()) / 1000)
  if (seconds < 60) return 'just now'
  const minutes = seconds / 60
  if (minutes < 60) return `${Math.floor(minutes)}m ago`
  const hours = minutes / 60
  if (hours < 24) return `${Math.floor(hours)}h ago`
  const days = hours / 24
  if (days < 30) return `${Math.floor(days)}d ago`
  return dateFormatter.format(then)
}
