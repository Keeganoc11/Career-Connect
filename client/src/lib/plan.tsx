import { useEffect, useState, type ReactNode } from 'react'
import { api, auth } from '../api/client'
import type { PlanTier } from '../api/types'
import { PlanContext } from './planContext'

/**
 * What this account is allowed to use, read once per app load.
 *
 * The stored plan is a first-paint hint so a Pro user never sees the Free
 * layout flash; /api/auth/me is the real answer, and it's fetched again rather
 * than trusted from login, because a plan can change while a token is still
 * valid. Nothing here is a security boundary — every Pro endpoint checks for
 * itself, and this only decides what to draw.
 */
export function PlanProvider({ children }: { children: ReactNode }) {
  const [plan, setPlan] = useState<PlanTier>(() => auth.plan)

  const refresh = async () => {
    try {
      const me = await api.me()
      auth.plan = me.plan
      setPlan(me.plan)
    } catch {
      // An expired session already signs the app out through the 401 handler,
      // and a network blip shouldn't downgrade what's on screen — keep the
      // last known plan and try again on the next load.
    }
  }

  useEffect(() => {
    void refresh()
    // Mount only: the token doesn't change without the app remounting.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <PlanContext.Provider value={{ plan, isPro: plan === 'Pro', refresh }}>
      {children}
    </PlanContext.Provider>
  )
}
