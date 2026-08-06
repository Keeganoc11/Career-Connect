import { useState } from 'react'
import { auth } from './api/client'
import { AppShell, type View } from './components/AppShell'
import { LoginPage } from './pages/LoginPage'
import { TrackerPage } from './pages/TrackerPage'
import { ResumesPage } from './pages/ResumesPage'

export default function App() {
  const [loggedIn, setLoggedIn] = useState(() => auth.token !== null)
  const [view, setView] = useState<View>('tracker')

  const signOut = () => {
    auth.clear()
    setLoggedIn(false)
  }

  if (!loggedIn) {
    return <LoginPage onLoggedIn={() => setLoggedIn(true)} />
  }

  return (
    <AppShell view={view} onViewChange={setView} onSignOut={signOut}>
      {view === 'tracker' ? (
        <TrackerPage onLoggedOut={() => setLoggedIn(false)} />
      ) : (
        <ResumesPage onLoggedOut={() => setLoggedIn(false)} />
      )}
    </AppShell>
  )
}
