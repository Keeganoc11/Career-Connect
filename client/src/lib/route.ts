import { useCallback, useEffect, useState } from 'react'

export type Tab = 'tailor' | 'agenda' | 'tracker' | 'resumes'

export type Route =
  | { view: Tab }
  | { view: 'job'; applicationId: string }
  | { view: 'interview'; interviewId: string }

const TAB_PATHS: Record<Tab, string> = {
  tailor: 'tailor',
  agenda: 'agenda',
  tracker: 'applications',
  resumes: 'resumes',
}

/**
 * The app's address in the URL hash: "#/applications", "#/jobs/<id>",
 * "#/interviews/<id>".
 *
 * A hash rather than a router: the API serves index.html for unknown paths,
 * but a hash needs no server cooperation at all, and there's one dynamic page.
 * What it buys is the back button and a job page you can reload or keep open.
 */
export function parseRoute(hash: string): Route {
  const [first, second] = hash.replace(/^#\/?/, '').split('/')

  if (first === 'jobs' && second) {
    return { view: 'job', applicationId: decodeURIComponent(second) }
  }

  if (first === 'interviews' && second) {
    return { view: 'interview', interviewId: decodeURIComponent(second) }
  }

  const tab = (Object.keys(TAB_PATHS) as Tab[]).find((key) => TAB_PATHS[key] === first)
  return { view: tab ?? 'tailor' }
}

export function routeHash(route: Route): string {
  if (route.view === 'job') return `#/jobs/${encodeURIComponent(route.applicationId)}`
  if (route.view === 'interview') return `#/interviews/${encodeURIComponent(route.interviewId)}`
  return `#/${TAB_PATHS[route.view]}`
}

/**
 * Whether this session has pushed an entry of its own. history.length can't
 * tell — it counts pages from before the app too — and "Back" on a job page
 * opened from a bookmark shouldn't leave the app.
 */
let navigatedInApp = false

export function canGoBackInApp(): boolean {
  return navigatedInApp
}

export function useRoute(): [Route, (route: Route) => void] {
  const [route, setRoute] = useState(() => parseRoute(window.location.hash))

  useEffect(() => {
    const sync = () => setRoute(parseRoute(window.location.hash))
    window.addEventListener('hashchange', sync)
    return () => window.removeEventListener('hashchange', sync)
  }, [])

  const navigate = useCallback((next: Route) => {
    const hash = routeHash(next)
    if (window.location.hash !== hash) {
      navigatedInApp = true
      // Assigning the hash pushes a history entry and fires hashchange, which
      // is what updates the state — one path for clicks and the back button.
      window.location.hash = hash
    }
    window.scrollTo({ top: 0 })
  }, [])

  return [route, navigate]
}
