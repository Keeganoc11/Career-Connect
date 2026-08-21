import { useState, type FormEvent } from 'react'
import { api, auth, ApiError } from '../api/client'
import { BrandMark } from '../components/AppShell'

const inputClass =
  'w-full rounded-xl border border-slate-300 px-4 py-3 text-base text-slate-900 placeholder:text-slate-400 focus:border-brand-500 focus:outline-none focus:ring-4 focus:ring-brand-500/15'

const highlights = [
  'Track every application in one pipeline',
  'Score your resume against any job description',
  'See exactly which requirements you are missing',
]

export function LoginPage({ onLoggedIn }: { onLoggedIn: () => void }) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const switchMode = (next: 'login' | 'register') => {
    setMode(next)
    setError(null)
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      auth.save(mode === 'login' ? await api.login(email, password) : await api.register(email, password, displayName))
      onLoggedIn()
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        setError('Invalid email or password.')
      } else if (e instanceof ApiError && e.status === 409) {
        setError('An account with that email already exists.')
      } else if (e instanceof ApiError && e.status === 429) {
        setError('Too many attempts — wait a minute and try again.')
      } else if (e instanceof ApiError && e.status !== 0) {
        setError(e.message)
      } else {
        setError('Could not reach the server. Is the API running on port 5199?')
      }
      setBusy(false)
    }
  }

  return (
    <main className="grid min-h-screen lg:grid-cols-2">
      {/* Brand panel — the gradient carries the identity on the way in. */}
      <section className="brand-gradient relative hidden flex-col justify-between overflow-hidden p-12 lg:flex">
        <div
          className="absolute -right-24 -top-24 size-96 rounded-full bg-white/10 blur-3xl"
          aria-hidden
        />
        <div
          className="absolute -bottom-32 -left-20 size-96 rounded-full bg-white/10 blur-3xl"
          aria-hidden
        />

        <div className="relative flex items-center gap-3">
          <div className="flex size-11 items-center justify-center rounded-xl bg-white/20 text-base font-extrabold text-white backdrop-blur">
            CC
          </div>
          <span className="text-xl font-bold tracking-tight text-white">Career Connect</span>
        </div>

        <div className="relative">
          <h1 className="max-w-md text-5xl font-bold leading-[1.1] tracking-tight text-white">
            Stop losing track of your job search.
          </h1>
          <ul className="mt-10 space-y-4">
            {highlights.map((item) => (
              <li key={item} className="flex items-start gap-3 text-lg text-white/90">
                <span
                  className="mt-2 size-2 shrink-0 rounded-full bg-white/70"
                  aria-hidden
                />
                {item}
              </li>
            ))}
          </ul>
        </div>

        <p className="relative text-sm text-white/70">
          Built with ASP.NET Core, Entity Framework, React, and the Claude API.
        </p>
      </section>

      <section className="flex items-center justify-center bg-white px-6 py-16">
        <div className="w-full max-w-md">
          <div className="mb-10 lg:hidden">
            <BrandMark size="lg" />
          </div>

          <h2 className="text-3xl font-bold tracking-tight text-slate-900">
            {mode === 'login' ? 'Welcome back' : 'Create your account'}
          </h2>
          <p className="mt-2 text-base text-slate-500">
            {mode === 'login' ? 'Sign in to pick up where you left off.' : 'Track your job search in one place.'}
          </p>

          <form onSubmit={submit} className="mt-8">
            {mode === 'register' && (
              <label className="mb-5 block">
                <span className="mb-1.5 block text-sm font-semibold text-slate-700">Name (optional)</span>
                <input
                  className={inputClass}
                  type="text"
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                  maxLength={200}
                  autoFocus
                />
              </label>
            )}

            <label className="block">
              <span className="mb-1.5 block text-sm font-semibold text-slate-700">Email</span>
              <input
                className={inputClass}
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                autoFocus={mode === 'login'}
              />
            </label>

            <label className="mt-5 block">
              <span className="mb-1.5 block text-sm font-semibold text-slate-700">Password</span>
              <input
                className={inputClass}
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                minLength={mode === 'register' ? 8 : undefined}
              />
              {mode === 'register' && (
                <span className="mt-1.5 block text-xs text-slate-400">At least 8 characters.</span>
              )}
            </label>

            {error && (
              <p className="mt-5 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700">
                {error}
              </p>
            )}

            <button
              type="submit"
              disabled={busy}
              className="brand-gradient mt-7 w-full rounded-xl px-4 py-3.5 text-base font-semibold text-white shadow-lg shadow-brand-600/25 transition hover:opacity-95 disabled:opacity-60"
            >
              {busy ? (mode === 'login' ? 'Signing in…' : 'Creating account…') : mode === 'login' ? 'Sign in' : 'Create account'}
            </button>

            <p className="mt-5 text-center text-sm text-slate-500">
              {mode === 'login' ? (
                <>
                  New here?{' '}
                  <button type="button" onClick={() => switchMode('register')} className="font-semibold text-brand-600 hover:underline">
                    Create an account
                  </button>
                </>
              ) : (
                <>
                  Already have an account?{' '}
                  <button type="button" onClick={() => switchMode('login')} className="font-semibold text-brand-600 hover:underline">
                    Sign in
                  </button>
                </>
              )}
            </p>
          </form>
        </div>
      </section>
    </main>
  )
}
