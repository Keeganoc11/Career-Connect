import { useEffect, useState, type FormEvent } from 'react'
import { api, auth, ApiError, UNREACHABLE_MESSAGE } from '../api/client'
import { Banner, BrandMark, Button, Field, Input } from '../components/ui'

export function LoginPage({ onLoggedIn }: { onLoggedIn: () => void }) {
  const [mode, setMode] = useState<'login' | 'register'>('login')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // AppShell isn't mounted out here, so this page sets its own title.
  useEffect(() => {
    document.title = 'Sign in · Career Connect'
  }, [])

  const switchMode = (next: 'login' | 'register') => {
    setMode(next)
    setError(null)
  }

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      auth.save(
        mode === 'login'
          ? await api.login(email, password)
          : await api.register(email, password, displayName),
      )
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
        setError(UNREACHABLE_MESSAGE)
      }
      setBusy(false)
    }
  }

  return (
    <main className="flex min-h-dvh items-center justify-center bg-surface-muted px-4 py-12">
      <div className="w-full max-w-sm">
        <div className="flex items-center gap-2">
          <BrandMark className="size-7" />
          <span className="text-sm font-semibold text-fg">Career Connect</span>
        </div>

        <h1 className="mt-8 text-xl font-semibold text-fg">
          {mode === 'login' ? 'Welcome back' : 'Create your account'}
        </h1>
        <p className="mt-1 text-sm text-fg-muted">
          {mode === 'login'
            ? 'Sign in to pick up where you left off.'
            : 'Track your job search in one place.'}
        </p>

        <form onSubmit={submit} className="mt-6 space-y-4">
          {mode === 'register' && (
            <Field label="Name (optional)">
              {(props) => (
                <Input
                  {...props}
                  type="text"
                  value={displayName}
                  onChange={(e) => setDisplayName(e.target.value)}
                  maxLength={200}
                  autoFocus
                />
              )}
            </Field>
          )}

          <Field label="Email">
            {(props) => (
              <Input
                {...props}
                type="email"
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                autoFocus={mode === 'login'}
              />
            )}
          </Field>

          <Field label="Password" hint={mode === 'register' ? 'At least 8 characters.' : undefined}>
            {(props) => (
              <Input
                {...props}
                type="password"
                autoComplete={mode === 'login' ? 'current-password' : 'new-password'}
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                minLength={mode === 'register' ? 8 : undefined}
              />
            )}
          </Field>

          {error && <Banner>{error}</Banner>}

          <Button variant="primary" type="submit" fullWidth loading={busy}>
            {busy
              ? mode === 'login'
                ? 'Signing in…'
                : 'Creating account…'
              : mode === 'login'
                ? 'Sign in'
                : 'Create account'}
          </Button>
        </form>

        <p className="mt-5 text-center text-sm text-fg-muted">
          {mode === 'login' ? 'New here? ' : 'Already have an account? '}
          <button
            type="button"
            onClick={() => switchMode(mode === 'login' ? 'register' : 'login')}
            className="rounded-sm font-medium text-accent hover:text-accent-hover"
          >
            {mode === 'login' ? 'Create an account' : 'Sign in'}
          </button>
        </p>
      </div>
    </main>
  )
}
