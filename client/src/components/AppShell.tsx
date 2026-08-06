import { useEffect, type ReactNode } from 'react'
import { auth } from '../api/client'

export type View = 'tracker' | 'resumes'

interface Props {
  view: View
  onViewChange: (view: View) => void
  onSignOut: () => void
  children: ReactNode
}

const tabs: { id: View; label: string; title: string }[] = [
  { id: 'tracker', label: 'Applications', title: 'Applications · Career Connect' },
  { id: 'resumes', label: 'Resumes', title: 'Resumes · Career Connect' },
]

export function BrandMark({ size = 'md' }: { size?: 'md' | 'lg' }) {
  const box = size === 'lg' ? 'size-14 text-xl rounded-2xl' : 'size-10 text-base rounded-xl'
  return (
    <div
      className={`brand-gradient flex items-center justify-center font-extrabold tracking-tight text-white shadow-lg shadow-brand-600/30 ${box}`}
      aria-hidden
    >
      CC
    </div>
  )
}

export function AppShell({ view, onViewChange, onSignOut, children }: Props) {
  // Keep the browser tab title in step with the current view.
  useEffect(() => {
    document.title = tabs.find((tab) => tab.id === view)?.title ?? 'Career Connect'
  }, [view])

  return (
    <div className="flex min-h-screen flex-col bg-slate-50">
      <header className="sticky top-0 z-20 border-b border-slate-200/80 bg-white/90 backdrop-blur">
        {/* Thin gradient rule ties the whole chrome to the brand. */}
        <div className="brand-gradient h-1 w-full" aria-hidden />

        <div className="mx-auto flex max-w-6xl items-center justify-between gap-6 px-5 py-4">
          <div className="flex items-center gap-8">
            <div className="flex items-center gap-3">
              <BrandMark />
              <div className="leading-tight">
                <div className="text-lg font-bold tracking-tight text-slate-900">
                  Career<span className="brand-text-gradient">Connect</span>
                </div>
                <div className="text-xs font-medium text-slate-500">Job search, tracked</div>
              </div>
            </div>

            <nav aria-label="Main" className="hidden items-center gap-1 sm:flex">
              {tabs.map((tab) => {
                const active = view === tab.id
                return (
                  <button
                    key={tab.id}
                    type="button"
                    onClick={() => onViewChange(tab.id)}
                    aria-current={active ? 'page' : undefined}
                    className={`relative rounded-xl px-4 py-2.5 text-base font-semibold transition ${
                      active
                        ? 'bg-brand-50 text-brand-700'
                        : 'text-slate-500 hover:bg-slate-100 hover:text-slate-800'
                    }`}
                  >
                    {tab.label}
                    {active && (
                      <span
                        className="brand-gradient absolute inset-x-3 -bottom-0.5 h-0.5 rounded-full"
                        aria-hidden
                      />
                    )}
                  </button>
                )
              })}
            </nav>
          </div>

          <div className="flex items-center gap-3">
            <span className="hidden text-sm font-medium text-slate-500 lg:inline">
              {auth.email}
            </span>
            <button
              type="button"
              onClick={onSignOut}
              className="rounded-xl border border-slate-200 px-4 py-2 text-sm font-semibold text-slate-600 transition hover:border-slate-300 hover:bg-slate-50 hover:text-slate-900"
            >
              Sign out
            </button>
          </div>
        </div>

        {/* Nav collapses to its own row on small screens rather than disappearing. */}
        <nav aria-label="Main" className="flex gap-1 border-t border-slate-100 px-5 py-2 sm:hidden">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              type="button"
              onClick={() => onViewChange(tab.id)}
              aria-current={view === tab.id ? 'page' : undefined}
              className={`flex-1 rounded-lg px-3 py-2 text-sm font-semibold transition ${
                view === tab.id ? 'bg-brand-50 text-brand-700' : 'text-slate-500'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </header>

      <div className="flex-1">{children}</div>

      <footer className="border-t border-slate-200 bg-white">
        <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-2 px-5 py-5 text-sm text-slate-500">
          <span>
            <span className="font-semibold text-slate-700">Career Connect</span> — built with
            ASP.NET&nbsp;Core, React, and Claude.
          </span>
          <span className="text-slate-400">Your data stays on this machine.</span>
        </div>
      </footer>
    </div>
  )
}
