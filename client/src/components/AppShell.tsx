import { useEffect, type ReactNode } from 'react'
import { Briefcase, CalendarDays, FileText, Lock, Mail, Sparkles } from 'lucide-react'
import { usePlan } from '../lib/planContext'
import type { Tab } from '../lib/route'
import type { GmailConnection } from '../lib/useGmailConnection'
import { AccountMenu } from './AccountMenu'
import { BrandMark, IconButton } from './ui'

interface Props {
  /** The highlighted tab. A job's page counts as Applications. */
  view: Tab
  /** False on a page that sets its own title, like a job's. */
  ownsTitle: boolean
  onViewChange: (view: Tab) => void
  gmail: GmailConnection
  onOpenEmailUpdates: () => void
  onSignOut: () => void
  children: ReactNode
}

const TABS: { id: Tab; label: string; title: string; icon: typeof CalendarDays; pro?: true }[] = [
  { id: 'tailor', label: 'Tailor', title: 'Tailor · Career Connect', icon: Sparkles, pro: true },
  { id: 'agenda', label: 'Agenda', title: 'Agenda · Career Connect', icon: CalendarDays },
  { id: 'tracker', label: 'Applications', title: 'Applications · Career Connect', icon: Briefcase },
  { id: 'resumes', label: 'Resumes', title: 'Resumes · Career Connect', icon: FileText, pro: true },
]

/**
 * One 56px row on desktop and a 48px bar plus a bottom tab bar on a phone,
 * replacing a stacked header that cost about 129px before any content —
 * a gradient rule, a brand block with a tagline, a nav row, and a second nav
 * row below it on small screens.
 */
export function AppShell({
  view,
  ownsTitle,
  onViewChange,
  gmail,
  onOpenEmailUpdates,
  onSignOut,
  children,
}: Props) {
  const { isPro } = usePlan()

  useEffect(() => {
    if (ownsTitle) document.title = TABS.find((tab) => tab.id === view)?.title ?? 'Career Connect'
  }, [view, ownsTitle])

  // Pro tabs stay in the bar on Free, with a lock, and lead to a page that
  // explains the feature. Removing them would leave nothing to upgrade for.
  const locked = (tab: (typeof TABS)[number]) => Boolean(tab.pro) && !isPro

  const emailUpdates = (
    <IconButton
      label={
        gmail.pendingCount > 0
          ? `Email updates, ${gmail.pendingCount} needing review`
          : 'Email updates'
      }
      icon={<Mail className="size-4" aria-hidden />}
      count={gmail.pendingCount}
      onClick={onOpenEmailUpdates}
    />
  )

  return (
    <div className="min-h-dvh bg-surface-muted">
      <header className="sticky top-0 z-20 border-b border-line bg-surface">
        <div className="mx-auto flex h-12 max-w-6xl items-center justify-between gap-4 px-4 sm:h-14">
          <div className="flex min-w-0 items-center gap-6">
            <span className="flex items-center gap-2">
              <BrandMark className="size-6" />
              <span className="text-sm font-semibold text-fg">Career Connect</span>
            </span>

            {/* Text tabs on desktop; the phone gets the bottom bar instead. */}
            <nav aria-label="Main" className="hidden items-center gap-1 sm:flex">
              {TABS.map((tab) => {
                const active = view === tab.id
                return (
                  <button
                    key={tab.id}
                    type="button"
                    onClick={() => onViewChange(tab.id)}
                    aria-current={active ? 'page' : undefined}
                    aria-label={locked(tab) ? `${tab.label} (Pro)` : undefined}
                    className={`relative rounded-control px-3 py-1.5 text-sm transition-colors ${
                      active ? 'font-medium text-fg' : 'text-fg-muted hover:text-fg'
                    }`}
                  >
                    {tab.label}
                    {locked(tab) && (
                      <Lock className="ml-1 inline-block size-3 align-[-1px] text-fg-subtle" aria-hidden />
                    )}
                    {active && (
                      <span
                        className="absolute inset-x-3 -bottom-[13px] h-0.5 rounded-full bg-accent"
                        aria-hidden
                      />
                    )}
                  </button>
                )
              })}
            </nav>
          </div>

          <div className="flex shrink-0 items-center gap-1">
            {/* Nothing scans a Free inbox, so there's never anything to review. */}
            {isPro && emailUpdates}
            <AccountMenu gmail={gmail} onSignOut={onSignOut} />
          </div>
        </div>
      </header>

      {/* Bottom padding on small screens clears the fixed tab bar. */}
      <main className="mx-auto max-w-6xl px-4 py-6 pb-24 sm:pb-8">{children}</main>

      <nav
        aria-label="Main"
        className="fixed inset-x-0 bottom-0 z-20 border-t border-line bg-surface pb-[env(safe-area-inset-bottom)] sm:hidden"
      >
        <div className="flex">
          {TABS.map((tab) => {
            const active = view === tab.id
            const Icon = tab.icon
            return (
              <button
                key={tab.id}
                type="button"
                onClick={() => onViewChange(tab.id)}
                aria-current={active ? 'page' : undefined}
                aria-label={locked(tab) ? `${tab.label} (Pro)` : undefined}
                className={`flex flex-1 flex-col items-center gap-0.5 py-2 text-xs transition-colors ${
                  active ? 'font-medium text-accent' : 'text-fg-muted'
                }`}
              >
                <span className="relative">
                  <Icon className="size-5" aria-hidden />
                  {locked(tab) && (
                    <Lock
                      className="absolute -right-1.5 -top-0.5 size-3 text-fg-subtle"
                      aria-hidden
                    />
                  )}
                </span>
                {tab.label}
              </button>
            )
          })}
        </div>
      </nav>
    </div>
  )
}
