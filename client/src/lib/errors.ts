import { ApiError } from '../api/client'

/**
 * What to show the user for a failed call — or null when there's nothing worth
 * saying. A 401 means the session ended, and api/client signs them out on its
 * own; rendering "Unauthorized" in a panel that's about to unmount is noise.
 */
export function errorMessage(e: unknown): string | null {
  if (e instanceof ApiError && e.status === 401) return null
  if (e instanceof Error) return e.message
  return 'Something went wrong.'
}
