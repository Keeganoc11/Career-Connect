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

const dateTimeFormatter = new Intl.DateTimeFormat('en-US', {
  weekday: 'short',
  month: 'short',
  day: 'numeric',
  hour: 'numeric',
  minute: '2-digit',
})

const timeFormatter = new Intl.DateTimeFormat('en-US', { hour: 'numeric', minute: '2-digit' })

function toDate(isoUtc: string): Date {
  return new Date(isoUtc.endsWith('Z') ? isoUtc : `${isoUtc}Z`)
}

/** "Thu, Sep 10, 2:00 PM" in the viewer's own timezone — interviews are wall-clock events. */
export function formatDateTime(isoUtc: string): string {
  return dateTimeFormatter.format(toDate(isoUtc))
}

/**
 * How far off something is, phrased for a schedule: "Tomorrow at 2:00 PM",
 * "in 3 days", "2h ago". formatRelative can't do this — it clamps to the past.
 */
export function formatUntil(isoUtc: string): string {
  const when = toDate(isoUtc)
  const diffMs = when.getTime() - Date.now()

  if (diffMs < 0) {
    return formatRelative(isoUtc)
  }

  const hours = diffMs / 3_600_000
  if (hours < 1) return `in ${Math.max(1, Math.round(diffMs / 60_000))}m`
  if (hours < 12) return `in ${Math.round(hours)}h`

  // Calendar days apart, not 24-hour blocks: something at 9am tomorrow is
  // "tomorrow" even when it's only 14 hours away.
  const startOfToday = new Date()
  startOfToday.setHours(0, 0, 0, 0)
  const startOfThen = new Date(when)
  startOfThen.setHours(0, 0, 0, 0)
  const days = Math.round((startOfThen.getTime() - startOfToday.getTime()) / 86_400_000)

  if (days === 0) return `today at ${timeFormatter.format(when)}`
  if (days === 1) return `tomorrow at ${timeFormatter.format(when)}`
  if (days < 14) return `in ${days} days`

  // Stays relative rather than falling back to a date: callers pair this with
  // the absolute date already, and repeating it there says nothing.
  const weeks = Math.round(days / 7)
  return `in ${weeks} week${weeks === 1 ? '' : 's'}`
}

/** UTC ISO → the "YYYY-MM-DDTHH:mm" local-time shape a datetime-local input wants. */
export function toDateTimeLocalValue(isoUtc: string): string {
  const local = toDate(isoUtc)
  const pad = (n: number) => String(n).padStart(2, '0')
  return (
    `${local.getFullYear()}-${pad(local.getMonth() + 1)}-${pad(local.getDate())}` +
    `T${pad(local.getHours())}:${pad(local.getMinutes())}`
  )
}

/** The inverse: a datetime-local value is local wall-clock, so let Date do the conversion. */
export function fromDateTimeLocalValue(value: string): string {
  return new Date(value).toISOString()
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
