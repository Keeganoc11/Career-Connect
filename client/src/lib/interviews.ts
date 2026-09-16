import type { Application, InterviewEvent, InterviewKind } from '../api/types'

/**
 * Sentence case, per the glossary — "Phone screen", never the wire value
 * "PhoneScreen". Defined once so the scheduling form, the agenda card and the
 * row chip can't drift apart.
 */
export const KIND_LABELS: Record<InterviewKind, string> = {
  PhoneScreen: 'Phone screen',
  Technical: 'Technical',
  Onsite: 'Onsite',
  Final: 'Final',
  Other: 'Other',
}

/**
 * The soonest interview still ahead — past rounds stay in the dialog. Shared by
 * the table and the phone list so the two can't disagree about which one is
 * "next".
 */
export function nextInterview(application: Application): InterviewEvent | undefined {
  const now = Date.now()
  return application.interviews
    .filter((i) => new Date(i.scheduledAtUtc).getTime() >= now)
    .sort((a, b) => a.scheduledAtUtc.localeCompare(b.scheduledAtUtc))[0]
}
