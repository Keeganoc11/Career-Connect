import type { FitVerdict, GapFix, GapSeverity, PrepRun } from '../api/types'

export const VERDICT_LABELS: Record<FitVerdict, string> = {
  StrongFit: 'Strong fit',
  WorthAShot: 'Worth a shot',
  Stretch: 'Stretch',
  NotAFit: 'Not a fit',
}

export const SEVERITY_LABELS: Record<GapSeverity, string> = {
  Dealbreaker: 'Dealbreaker',
  Fixable: 'Fixable',
  Minor: 'Minor',
}

export const FIX_LABELS: Record<GapFix, string> = {
  Resume: 'Fix on the resume',
  Interview: 'Cover it in the interview',
  BuildSkill: 'Build the skill',
}

/** One short phrase for where a job's tailoring stands, for lists. */
export function tailoringSummary(run: PrepRun | undefined): string {
  if (!run) return 'Not tailored'
  if (run.status === 'Running') return run.steps.at(-1)?.label ?? 'Starting…'
  if (run.status === 'Failed') return "Didn't finish"
  if (run.review) return `${VERDICT_LABELS[run.review.verdict]} · ${run.baselineScore} → ${run.finalScore}`
  // Prepped before tailoring kept the PDF format — there's no file to download.
  return 'Needs a fresh run'
}
