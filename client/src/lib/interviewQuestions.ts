import type { AnswerQuality, InterviewQuestionKind } from '../api/types'

/** Sentence case, per the glossary — the wire value is "SystemDesign". */
export const QUESTION_KIND_LABELS: Record<InterviewQuestionKind, string> = {
  Behavioral: 'Behavioral',
  Technical: 'Technical',
  SystemDesign: 'System design',
  Role: 'The role',
  Company: 'The company',
  Logistics: 'Logistics',
  Other: 'Other',
}

/**
 * How an answer went, in the words someone would use about their own answer.
 * Quality is never shown as a colored chip on its own: "Weak" beside a question
 * has to read as your own note, not as a status the app assigned you.
 */
export const QUALITY_LABELS: Record<AnswerQuality, string> = {
  Strong: 'Went well',
  Okay: 'Okay',
  Weak: 'Fumbled it',
}

/** Same bands as a match score, so 60 means the same thing in both places. */
export function debriefBand(score: number): { label: string; text: string; bar: string; track: string } {
  if (score >= 80) {
    return { label: 'Strong round', text: 'text-success', bar: 'bg-success', track: 'bg-success-soft' }
  }
  if (score >= 65) {
    return { label: 'Solid', text: 'text-fg', bar: 'bg-accent', track: 'bg-accent-soft' }
  }
  if (score >= 50) {
    return { label: 'Patchy', text: 'text-warning', bar: 'bg-warning', track: 'bg-warning-soft' }
  }
  return { label: 'Rough', text: 'text-danger', bar: 'bg-danger', track: 'bg-danger-soft' }
}
