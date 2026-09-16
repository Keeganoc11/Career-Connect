import type { ApplicationInput } from '../api/types'

type Prefill = Partial<Pick<ApplicationInput, 'companyName' | 'roleTitle' | 'dateApplied'>>

/**
 * A request for the tracker to open something, carried as state rather than a
 * route. The app has no router, and the alternative — every caller reaching
 * into TrackerPage's dialog state — is what left it with eight separate
 * `*Target` states.
 *
 * Written as an explicit union rather than derived with Omit: Omit doesn't
 * distribute over a union, so it would flatten these down to the one property
 * they share and lose `prefill` and `applicationId`.
 */
export type TrackerIntentRequest =
  | { kind: 'new'; prefill?: Prefill }
  | { kind: 'edit'; applicationId: string }
  | { kind: 'prep'; applicationId: string }
  | { kind: 'interviews'; applicationId: string }
  | { kind: 'interviewPrep'; applicationId: string }
  | { kind: 'coverLetter'; applicationId: string }
  | { kind: 'open'; applicationId: string }

/**
 * `id` makes each intent distinct, so asking for the same thing twice reopens
 * it instead of being swallowed as an unchanged value.
 */
export type TrackerIntent = TrackerIntentRequest & { id: number }

let nextId = 1

export function intent(request: TrackerIntentRequest): TrackerIntent {
  return { ...request, id: nextId++ }
}
