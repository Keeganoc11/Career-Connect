import { useEffect, useState } from 'react'
import { auth, setUnauthorizedHandler } from './api/client'
import type { LoginResponse } from './api/types'
import { PlanProvider } from './lib/plan'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'
import { Workspace } from './components/Workspace'

/**
 * The signed-out hash, which is only ever one of three things. The signed-in
 * app has its own router (lib/route); these live here because they exist
 * before it is mounted.
 */
type PublicView = 'home' | 'signin' | 'join'

function publicView(hash: string): PublicView {
  const path = hash.replace(/^#\/?/, '')
  if (path === 'signin') return 'signin'
  if (path === 'join') return 'join'
  return 'home'
}

/**
 * The auth gate, and the public front page in front of it. Navigation and the Gmail connection moved
 * into Workspace, which also removed the special case that landed the app on
 * the tracker when returning from Google — the OAuth return is handled by
 * useGmailConnection wherever you happen to be, so the app always opens on the
 * agenda.
 */
export default function App() {
  const [loggedIn, setLoggedIn] = useState(() => auth.token !== null)
  const [view, setView] = useState<PublicView>(() => publicView(window.location.hash))

  useEffect(() => {
    const sync = () => {
      setView(publicView(window.location.hash))
      // These are separate pages, not anchors on one — arriving at the sign-in
      // form from halfway down the home page shouldn't keep that scroll.
      window.scrollTo({ top: 0 })
    }
    window.addEventListener('hashchange', sync)
    return () => window.removeEventListener('hashchange', sync)
  }, [])

  // One place decides what an expired session means. api/client clears the
  // token and calls this; it only fires for requests that actually carried
  // one, so a wrong password at the login screen is unaffected.
  useEffect(() => {
    setUnauthorizedHandler(() => setLoggedIn(false))
    return () => setUnauthorizedHandler(null)
  }, [])

  const go = (hash: string) => {
    window.location.hash = hash
  }

  const signedIn = (login: LoginResponse) => {
    // Straight to the part of the app that account can actually use: a Free
    // account landing on the locked Tailor tab would be a strange welcome.
    go(login.plan === 'Pro' ? '#/tailor' : '#/applications')
    setLoggedIn(true)
  }

  if (!loggedIn) {
    if (view === 'home') {
      return <HomePage onSignIn={() => go('#/signin')} onGetStarted={() => go('#/join')} />
    }

    return (
      <LoginPage
        mode={view === 'join' ? 'register' : 'login'}
        onModeChange={(next) => go(next === 'register' ? '#/join' : '#/signin')}
        onBack={() => go('#/')}
        onLoggedIn={signedIn}
      />
    )
  }

  // The plan is only fetched for a signed-in session, so the provider lives
  // inside the gate rather than around it.
  return (
    <PlanProvider>
      <Workspace
        onSignOut={() => {
          auth.clear()
          setLoggedIn(false)
        }}
      />
    </PlanProvider>
  )
}
