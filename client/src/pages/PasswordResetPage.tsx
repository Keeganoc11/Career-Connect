import { useEffect, useState, type FormEvent } from 'react'
import { ArrowLeft, MailCheck } from 'lucide-react'
import { api, ApiError, UNREACHABLE_MESSAGE } from '../api/client'
import { Banner, BrandMark, Button, Field, Input } from '../components/ui'

interface Props {
  /** "request" asks for the link; "set" redeems the token from the URL. */
  mode: 'request' | 'set'
  /** Present only in "set" mode, read from ?token= on the reset link. */
  token: string | null
  onBackToSignIn: () => void
}

/**
 * Both halves of "I forgot my password", sharing the sign-in page's layout.
 *
 * Asking for a link always says the same thing back, whether or not that
 * address has an account — the server is deliberately silent about which, and
 * a helpful "no account found" here would give that away again.
 */
export function PasswordResetPage({ mode, token, onBackToSignIn }: Props) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [sent, setSent] = useState(false)
  const [done, setDone] = useState(false)

  useEffect(() => {
    document.title = 'Reset your password · Career Connect'
  }, [])

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setBusy(true)
    setError(null)

    try {
      if (mode === 'request') {
        await api.requestPasswordReset(email)
        setSent(true)
      } else {
        await api.resetPassword(token ?? '', password)
        setDone(true)
      }
    } catch (e) {
      if (e instanceof ApiError && e.status === 429) {
        setError('Too many attempts — wait a minute and try again.')
      } else if (e instanceof ApiError && e.status !== 0) {
        setError(e.message)
      } else {
        setError(UNREACHABLE_MESSAGE)
      }
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="flex min-h-dvh items-center justify-center bg-surface-muted px-4 py-12">
      <div className="w-full max-w-sm">
        <span className="flex items-center gap-2">
          <BrandMark className="size-7" />
          <span className="text-sm font-semibold text-fg">Career Connect</span>
        </span>

        {sent ? (
          <Confirmation
            title="Check your email"
            body={`If ${email} has an account, a link to set a new password is on its way. It works once and expires in an hour.`}
            onBackToSignIn={onBackToSignIn}
          />
        ) : done ? (
          <Confirmation
            title="Password changed"
            body="Everywhere you were signed in has been signed out. Sign in with your new password."
            onBackToSignIn={onBackToSignIn}
          />
        ) : mode === 'set' && !token ? (
          <Confirmation
            title="That link is incomplete"
            body="Open the link from the email exactly as it was sent, or ask for a new one."
            onBackToSignIn={onBackToSignIn}
          />
        ) : (
          <>
            <h1 className="mt-8 text-xl font-semibold text-fg">
              {mode === 'request' ? 'Forgot your password?' : 'Set a new password'}
            </h1>
            <p className="mt-1 text-sm text-fg-muted">
              {mode === 'request'
                ? 'We’ll email you a link to set a new one.'
                : 'Pick something you don’t use anywhere else.'}
            </p>

            <form onSubmit={submit} className="mt-6 space-y-4">
              {mode === 'request' ? (
                <Field label="Email">
                  {(props) => (
                    <Input
                      {...props}
                      type="email"
                      autoComplete="email"
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      required
                      autoFocus
                    />
                  )}
                </Field>
              ) : (
                <Field label="New password" hint="At least 8 characters.">
                  {(props) => (
                    <Input
                      {...props}
                      type="password"
                      autoComplete="new-password"
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      required
                      minLength={8}
                      autoFocus
                    />
                  )}
                </Field>
              )}

              {error && <Banner>{error}</Banner>}

              <Button variant="primary" type="submit" fullWidth loading={busy}>
                {mode === 'request' ? 'Email me a link' : 'Set new password'}
              </Button>
            </form>

            <p className="mt-6 text-center">
              <BackLink onClick={onBackToSignIn} />
            </p>
          </>
        )}
      </div>
    </main>
  )
}

function Confirmation({
  title,
  body,
  onBackToSignIn,
}: {
  title: string
  body: string
  onBackToSignIn: () => void
}) {
  return (
    <>
      <div className="mt-8 flex items-start gap-3">
        <span
          className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent"
          aria-hidden
        >
          <MailCheck className="size-4" />
        </span>
        <div>
          <h1 className="text-xl font-semibold text-fg">{title}</h1>
          <p className="mt-1 text-sm leading-relaxed text-fg-muted">{body}</p>
        </div>
      </div>
      <p className="mt-6 text-center">
        <BackLink onClick={onBackToSignIn} />
      </p>
    </>
  )
}

function BackLink({ onClick }: { onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="inline-flex items-center gap-1.5 rounded-sm text-sm text-fg-muted hover:text-fg"
    >
      <ArrowLeft className="size-4" aria-hidden />
      Back to sign in
    </button>
  )
}
