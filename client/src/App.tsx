import { useState } from 'react'
import { auth } from './api/client'
import { LoginPage } from './pages/LoginPage'
import { TrackerPage } from './pages/TrackerPage'

export default function App() {
  const [loggedIn, setLoggedIn] = useState(() => auth.token !== null)

  return loggedIn ? (
    <TrackerPage onLoggedOut={() => setLoggedIn(false)} />
  ) : (
    <LoginPage onLoggedIn={() => setLoggedIn(true)} />
  )
}
