import { useEffect } from 'react'
import {
  CalendarCheck,
  Check,
  ClipboardPaste,
  FileText,
  Inbox,
  MessageSquareText,
  ScanSearch,
  Send,
} from 'lucide-react'
import { Badge, BrandMark, Button, Card } from '../components/ui'
import {
  FREE_FEATURES,
  PRO_AVAILABILITY,
  PRO_FEATURES,
  PRO_PRICE,
  PRO_PRICE_PERIOD,
} from '../lib/tiers'

interface Props {
  onSignIn: () => void
  onGetStarted: () => void
}

const STEPS = [
  {
    icon: ClipboardPaste,
    title: 'Paste the job description',
    body: 'From LinkedIn, Indeed or a careers page. The company and role are read out of it, so there’s no form between finding a job and working on it.',
  },
  {
    icon: FileText,
    title: 'Get a resume written for that job',
    body: 'Your own resume, in your own format, with the wording rewritten for what the posting asks for — and a blunt read on where you actually stand.',
  },
  {
    icon: Inbox,
    title: 'Let the replies update themselves',
    body: 'Career Connect watches for replies, moves the job along when one lands, and puts the interview on your calendar.',
  },
]

const FEATURES = [
  {
    icon: ScanSearch,
    title: 'A score you can argue with',
    body: 'Every tailoring pass scores the resume against the posting and says what’s missing — including when the honest answer is that you don’t match.',
  },
  {
    icon: Send,
    title: 'Nothing is invented',
    body: 'Bullets get reworded, never upgraded. No tool, number or responsibility appears that isn’t already in your resume or the facts you added.',
  },
  {
    icon: CalendarCheck,
    title: 'Interviews on your calendar',
    body: 'An interview email becomes a calendar event and an agenda entry, with the round it belongs to.',
  },
  {
    icon: MessageSquareText,
    title: 'Interview prep that remembers',
    body: 'Company research, the questions they asked, the ones you want to ask, and a debrief scored against the job description.',
  },
]

/**
 * The public front page — what anyone who isn't signed in lands on.
 *
 * It exists to answer two questions before the sign-up form does: what this
 * does, and what the free version is. The pricing here and the upgrade panels
 * inside the app read from the same list, so they can't drift apart.
 */
export function HomePage({ onSignIn, onGetStarted }: Props) {
  useEffect(() => {
    document.title = 'Career Connect · Your job search, tracked and tailored'
  }, [])

  return (
    <div className="min-h-dvh bg-surface">
      <header className="sticky top-0 z-20 border-b border-line bg-surface">
        <div className="mx-auto flex h-14 max-w-5xl items-center justify-between gap-4 px-4">
          <span className="flex shrink-0 items-center gap-2">
            <BrandMark className="size-6" />
            <span className="text-sm font-semibold whitespace-nowrap text-fg">Career Connect</span>
          </span>
          <div className="flex items-center gap-2">
            {/* Two buttons don't fit beside the name on a phone, and the hero
                has both a few hundred pixels below this. */}
            <span className="hidden sm:block">
              <Button onClick={onSignIn}>Sign in</Button>
            </span>
            <Button variant="primary" onClick={onGetStarted}>
              Get started
            </Button>
          </div>
        </div>
      </header>

      <main>
        <section className="mx-auto max-w-5xl px-4 pt-16 pb-14 sm:pt-24 sm:pb-20">
          <div className="max-w-2xl">
            <h1 className="text-3xl leading-tight font-semibold text-balance text-fg sm:text-5xl">
              Stop rewriting your resume for every job
            </h1>
            <p className="mt-4 text-base leading-relaxed text-fg-muted sm:text-lg">
              Career Connect keeps every application in one place, and — when you want it to —
              rewrites your resume for the job in front of you, tells you honestly how you stack up,
              and watches your inbox so the tracker keeps itself up to date.
            </p>
            <div className="mt-8 flex flex-wrap items-center gap-3">
              <Button variant="primary" onClick={onGetStarted}>
                Start tracking free
              </Button>
              <Button onClick={onSignIn}>Sign in</Button>
            </div>
            <p className="mt-3 text-sm text-fg-subtle">
              The tracker is free and always will be. No card to start.
            </p>
          </div>
        </section>

        <section className="border-t border-line bg-surface-muted py-14 sm:py-20">
          <div className="mx-auto max-w-5xl px-4">
            <h2 className="text-xl font-semibold text-fg sm:text-2xl">How it works</h2>
            <ol className="mt-8 grid gap-6 sm:grid-cols-3">
              {STEPS.map((step, index) => {
                const Icon = step.icon
                return (
                  <li key={step.title}>
                    <span
                      className="flex size-9 items-center justify-center rounded-full bg-accent-soft text-accent"
                      aria-hidden
                    >
                      <Icon className="size-4" />
                    </span>
                    <h3 className="mt-3 text-sm font-semibold text-fg">
                      {index + 1}. {step.title}
                    </h3>
                    <p className="mt-1 text-sm leading-relaxed text-fg-muted">{step.body}</p>
                  </li>
                )
              })}
            </ol>
          </div>
        </section>

        <section className="py-14 sm:py-20">
          <div className="mx-auto max-w-5xl px-4">
            <h2 className="text-xl font-semibold text-fg sm:text-2xl">
              Built for the part nobody enjoys
            </h2>
            <p className="mt-2 max-w-2xl text-sm leading-relaxed text-fg-muted">
              Written while job hunting, against real postings — which is why it tells you when a job
              isn’t worth your afternoon.
            </p>
            <div className="mt-8 grid gap-4 sm:grid-cols-2">
              {FEATURES.map((feature) => {
                const Icon = feature.icon
                return (
                  <Card key={feature.title}>
                    <div className="flex items-start gap-3">
                      <Icon className="mt-0.5 size-4 shrink-0 text-accent" aria-hidden />
                      <div>
                        <h3 className="text-sm font-semibold text-fg">{feature.title}</h3>
                        <p className="mt-1 text-sm leading-relaxed text-fg-muted">{feature.body}</p>
                      </div>
                    </div>
                  </Card>
                )
              })}
            </div>
          </div>
        </section>

        <section id="pricing" className="border-t border-line bg-surface-muted py-14 sm:py-20">
          <div className="mx-auto max-w-5xl px-4">
            <h2 className="text-xl font-semibold text-fg sm:text-2xl">Pricing</h2>
            <p className="mt-2 text-sm text-fg-muted">
              Track your search for nothing. Pay only for the part that does the work for you.
            </p>

            <div className="mt-8 grid items-start gap-4 sm:grid-cols-2">
              <PlanCard
                name="Free"
                price="$0"
                period="forever"
                summary="A job tracker that stays out of your way."
                features={FREE_FEATURES}
                action={<Button fullWidth onClick={onGetStarted}>Create an account</Button>}
              />
              <PlanCard
                name="Pro"
                price={PRO_PRICE}
                period={PRO_PRICE_PERIOD}
                summary="Everything in Free, plus the parts that write and watch for you."
                features={PRO_FEATURES}
                highlighted
                action={<p className="text-sm text-fg-muted">{PRO_AVAILABILITY}</p>}
              />
            </div>
          </div>
        </section>
      </main>

      <footer className="border-t border-line py-8">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center justify-between gap-3 px-4">
          <span className="flex items-center gap-2">
            <BrandMark className="size-5" />
            <span className="text-sm text-fg-muted">Career Connect</span>
          </span>
          <button
            type="button"
            onClick={onSignIn}
            className="rounded-sm text-sm font-medium text-accent hover:text-accent-hover"
          >
            Sign in
          </button>
        </div>
      </footer>
    </div>
  )
}

function PlanCard({
  name,
  price,
  period,
  summary,
  features,
  action,
  highlighted = false,
}: {
  name: string
  price: string
  period: string
  summary: string
  features: string[]
  action: React.ReactNode
  /** The accent ring goes on Pro — the one place on this page color says "this one". */
  highlighted?: boolean
}) {
  return (
    <div
      className={`rounded-surface border bg-surface p-5 ${
        highlighted ? 'border-accent/30 shadow-raised' : 'border-line'
      }`}
    >
      <div className="flex items-center gap-2">
        <h3 className="text-sm font-semibold text-fg">{name}</h3>
        {highlighted && <Badge emphasis="accent">Automated</Badge>}
      </div>
      <p className="mt-3">
        <span className="text-3xl font-semibold text-fg">{price}</span>{' '}
        <span className="text-sm text-fg-muted">{period}</span>
      </p>
      <p className="mt-2 text-sm text-fg-muted">{summary}</p>

      <ul className="mt-5 space-y-2">
        {features.map((feature) => (
          <li key={feature} className="flex items-start gap-2 text-sm text-fg">
            <Check className="mt-0.5 size-4 shrink-0 text-accent" aria-hidden />
            {feature}
          </li>
        ))}
      </ul>

      <div className="mt-6">{action}</div>
    </div>
  )
}
