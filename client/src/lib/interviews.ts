import type { InterviewKind } from '../api/types'

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
