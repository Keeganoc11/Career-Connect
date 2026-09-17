import { createContext, useContext } from 'react'
import type { PlanTier } from '../api/types'

export interface PlanState {
  plan: PlanTier
  isPro: boolean
  /** Re-reads the plan from the server — call after anything that could change it. */
  refresh: () => Promise<void>
}

/**
 * Split from the provider so that file only exports a component, which is what
 * fast refresh needs to swap it without reloading the page.
 */
export const PlanContext = createContext<PlanState>({
  plan: 'Free',
  isPro: false,
  refresh: async () => {},
})

export function usePlan(): PlanState {
  return useContext(PlanContext)
}
