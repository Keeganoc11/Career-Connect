import { useState, type FormEvent } from 'react'
import { api, auth, ApiError } from '../api/client'

const inputClass =
  'w-full rounded-lg border border-slate-300 px-3 py-2 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-500 focus:outline-none focus:ring-2 focus:ring-indigo-500/30'

export function LoginPage({ onLoggedIn }: { onLoggedIn: () => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      auth.save(await api.login(email, password))
      onLoggedIn()
    } catch (e) {
      setError(
        e instanceof ApiError && e.status === 401
          ? 'Invalid email or password.'
          : 'Could not reach the server. Is the API running?',
      )
      setBusy(false)
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <div className="mb-6 text-center">
          <div className="mx-auto mb-3 flex size-11 items-center justify-center rounded-xl bg-indigo-600 text-lg font-bold text-white shadow-sm">
            CC
          </div>
          <h1 className="text-xl font-semibold text-slate-900">Career Connect</h1>
          <p className="mt-1 text-sm text-slate-500">Track every application in one place.</p>
        </div>

        <form onSubmit={submit} className="rounded-2xl bg-white p-6 shadow-sm ring-1 ring-slate-200">
          <label className="block text-sm">
            <span className="mb-1 block font-medium text-slate-700">Email</span>
            <input
              className={inputClass}
              type="email"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              autoFocus
            />
          </label>
          <label className="mt-4 block text-sm">
            <span className="mb-1 block font-medium text-slate-700">Password</span>
            <input
              className={inputClass}
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
            />
          </label>

          {error && (
            <p className="mt-4 rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>
          )}

          <button
            type="submit"
            disabled={busy}
            className="mt-5 w-full rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-60"
          >
            {busy ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </div>
    </main>
  )
}
