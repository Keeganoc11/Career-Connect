import type { Application, MatchResult } from '../api/types'
import { STATUS_ORDER } from './status'

export type SortKey = 'companyName' | 'status' | 'matchScore' | 'dateApplied' | 'updatedAtUtc'

/**
 * Shared by the table and the phone list, so the two can't drift into
 * disagreeing about what "sorted by match" means.
 */
export function sortApplications(
  applications: Application[],
  matches: Record<string, MatchResult>,
  sortKey: SortKey,
  ascending: boolean,
): Application[] {
  const direction = ascending ? 1 : -1

  const compare = (a: Application, b: Application): number => {
    switch (sortKey) {
      case 'companyName':
        return a.companyName.localeCompare(b.companyName) * direction
      case 'status':
        return (STATUS_ORDER[a.status] - STATUS_ORDER[b.status]) * direction
      case 'dateApplied':
        return a.dateApplied.localeCompare(b.dateApplied) * direction
      case 'updatedAtUtc':
        return a.updatedAtUtc.localeCompare(b.updatedAtUtc) * direction
      case 'matchScore': {
        // Unscored rows sort below every scored one in both directions, so the
        // direction applies only when both rows actually have a score.
        // Reversing the whole list afterwards used to float them to the top.
        const scoreA = matches[a.id]?.score
        const scoreB = matches[b.id]?.score
        if (scoreA === undefined) return scoreB === undefined ? 0 : 1
        if (scoreB === undefined) return -1
        return (scoreA - scoreB) * direction
      }
    }
  }

  return [...applications].sort(compare)
}
