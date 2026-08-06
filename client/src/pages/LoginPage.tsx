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
          : 'Could not reach the server. Is the API running on port 5199?',
      )
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

          <h2 className="text-3xl font-bold tracking-tight text-slate-900">Welcome back</h2>
          <p className="mt-2 text-base text-slate-500">Sign in to pick up where you left off.</p>

          <form onSubmit={submit} className="mt-8">
            <label className="block">
              <span className="mb-1.5 block text-sm font-semibold text-slate-700">Email</span>
              <input
                className={inputClass}
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                autoFocus
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
              />
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
              {busy ? 'Signing in…' : 'Sign in'}
            </button>
          </form>
        </div>
      </section>
    </main>
  )
}
