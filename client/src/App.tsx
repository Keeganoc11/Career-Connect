import { useState } from 'react'
import { auth } from './api/client'
import { AppShell, type View } from './components/AppShell'
import { LoginPage } from './pages/LoginPage'
import { AgendaPage } from './pages/AgendaPage'
import { TrackerPage } from './pages/TrackerPage'
import { ResumesPage } from './pages/ResumesPage'

/**
 * Agenda is the natural landing — "what needs attention today" is why you open
 * a job tracker. The exception is Google's OAuth redirect: TrackerPage reads
 * ?gmail= off the URL to finish connecting, so it has to be the mounted view
 * when we come back from consent.
 */
function initialView(): View {
  return new URLSearchParams(window.location.search).has('gmail') ? 'tracker' : 'agenda'
}

export default function App() {
  const [loggedIn, setLoggedIn] = useState(() => auth.token !== null)
  const [view, setView] = useState<View>(initialView)

  const signOut = () => {
    auth.clear()
    setLoggedIn(false)
  }

  if (!loggedIn) {
    return <LoginPage onLoggedIn={() => setLoggedIn(true)} />
  }

  const onLoggedOut = () => setLoggedIn(false)

  return (
    <AppShell view={view} onViewChange={setView} onSignOut={signOut}>
      {view === 'agenda' && (
        <AgendaPage onLoggedOut={onLoggedOut} onOpenTracker={() => setView('tracker')} />
      )}
      {view === 'tracker' && <TrackerPage onLoggedOut={onLoggedOut} />}
      {view === 'resumes' && <ResumesPage onLoggedOut={onLoggedOut} />}
    </AppShell>
  )
}
