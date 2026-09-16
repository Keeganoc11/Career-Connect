import { useEffect, useState } from 'react'
import { auth, setUnauthorizedHandler } from './api/client'
import { LoginPage } from './pages/LoginPage'
import { Workspace } from './components/Workspace'

/**
 * Nothing but the auth gate now. Navigation and the Gmail connection moved
 * into Workspace, which also removed the special case that landed the app on
 * the tracker when returning from Google — the OAuth return is handled by
 * useGmailConnection wherever you happen to be, so the app always opens on the
 * agenda.
 */
export default function App() {
  const [loggedIn, setLoggedIn] = useState(() => auth.token !== null)

  // One place decides what an expired session means. api/client clears the
  // token and calls this; it only fires for requests that actually carried
  // one, so a wrong password at the login screen is unaffected.
  useEffect(() => {
    setUnauthorizedHandler(() => setLoggedIn(false))
    return () => setUnauthorizedHandler(null)
  }, [])

  if (!loggedIn) {
    return <LoginPage onLoggedIn={() => setLoggedIn(true)} />
  }

  return (
    <Workspace
      onSignOut={() => {
        auth.clear()
        setLoggedIn(false)
      }}
    />
  )
}
