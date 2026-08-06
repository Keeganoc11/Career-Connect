interface ScoreBand {
  label: string
  /** Text colour for the numeral. */
  text: string
  /** Filled portion of the meter. */
  bar: string
  /** Meter track behind the fill. */
  track: string
  badge: string
}

/**
 * Three bands, each always rendered with its numeral and label — the colour is
 * a secondary cue, never the only one.
 */
export function scoreBand(score: number): ScoreBand {
  if (score >= 80) {
    return {
      label: 'Strong match',
      text: 'text-emerald-700',
      bar: 'bg-emerald-500',
      track: 'bg-emerald-100',
      badge: 'bg-emerald-50 text-emerald-800 ring-emerald-600/20',
    }
  }
  if (score >= 60) {
    return {
      label: 'Partial match',
      text: 'text-amber-700',
      bar: 'bg-amber-500',
      track: 'bg-amber-100',
      badge: 'bg-amber-50 text-amber-800 ring-amber-600/20',
    }
  }
  return {
    label: 'Weak match',
    text: 'text-rose-700',
    bar: 'bg-rose-500',
    track: 'bg-rose-100',
    badge: 'bg-rose-50 text-rose-800 ring-rose-600/20',
  }
}
