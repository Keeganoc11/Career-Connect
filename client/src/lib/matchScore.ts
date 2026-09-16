interface ScoreBand {
  label: string
  /** Colour for the numeral. */
  text: string
  /** Filled portion of the meter. */
  bar: string
  /** Meter track behind the fill. */
  track: string
}

/**
 * Three bands, each always rendered with its numeral and label — the colour is
 * a secondary cue, never the only one.
 *
 * On the role tokens rather than raw shades: a score is a measurement, so it
 * borrows the feedback colours for meaning but never renders as a chip, which
 * is reserved for statuses.
 */
export function scoreBand(score: number): ScoreBand {
  if (score >= 80) {
    return {
      label: 'Strong match',
      text: 'text-success',
      bar: 'bg-success',
      track: 'bg-success-soft',
    }
  }
  if (score >= 60) {
    return {
      label: 'Partial match',
      text: 'text-warning',
      bar: 'bg-warning',
      track: 'bg-warning-soft',
    }
  }
  return {
    label: 'Weak match',
    text: 'text-danger',
    bar: 'bg-danger',
    track: 'bg-danger-soft',
  }
}
