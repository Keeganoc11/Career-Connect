import { useEffect, useRef, useState } from 'react'
import { api, ApiError } from '../api/client'
import type { Application, PrepRun } from '../api/types'
import { ModalBackdrop, ModalHeader } from './Modal'

interface Props {
  application: Application
  /** Latest known run, if the tracker already loaded one. */
  run: PrepRun | null
  /** Start prep immediately on open — used when adding an application kicks the pipeline off itself. */
  autoStart?: boolean
  onRunChange: (run: PrepRun) => void
  onMarkApplied: () => void
  onClose: () => void
}

const POLL_INTERVAL_MS = 2500

function ScorePill({ score, target }: { score: number; target: number }) {
  const cleared = score >= target
  return (
    <span
      className={`inline-flex shrink-0 items-center rounded-full px-2.5 py-0.5 text-sm font-bold tabular-nums ring-1 ring-inset ${
        cleared
          ? 'bg-emerald-50 text-emerald-700 ring-emerald-600/20'
          : 'bg-amber-50 text-amber-800 ring-amber-600/20'
      }`}
    >
      {score}
    </span>
  )
}

function DocumentPanel({
  title,
  value,
  onChange,
  rows,
}: {
  title: string
  value: string
  onChange: (next: string) => void
  rows: number
}) {
  const [copied, setCopied] = useState(false)

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard permission can be denied; the text stays selectable on screen.
    }
  }

  return (
    <div>
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-base font-bold text-slate-900">{title}</h3>
        <button
          type="button"
          onClick={() => void copy()}
          className={`shrink-0 rounded-lg px-3 py-1.5 text-xs font-bold transition ${
            copied
              ? 'bg-emerald-50 text-emerald-700 ring-1 ring-inset ring-emerald-600/20'
              : 'bg-brand-600 text-white hover:bg-brand-700'
          }`}
        >
          {copied ? 'Copied ✓' : 'Copy'}
        </button>
      </div>
      <textarea
        className="mt-2 w-full rounded-xl border border-slate-300 bg-white p-3.5 font-mono text-sm leading-relaxed text-slate-800 focus:border-brand-500 focus:ring-4 focus:ring-brand-500/15 focus:outline-none"
        rows={rows}
        value={value}
        onChange={(e) => onChange(e.target.value)}
      />
    </div>
  )
}

export function PrepModal({ application, run, autoStart, onRunChange, onMarkApplied, onClose }: Props) {
  const [current, setCurrent] = useState<PrepRun | null>(run)
  const [starting, setStarting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const [resumeText, setResumeText] = useState(application.tailoredResumeText ?? '')
  const [coverLetterText, setCoverLetterText] = useState(application.coverLetterText ?? '')
  const [saving, setSaving] = useState(false)
  const [savedAt, setSavedAt] = useState<number | null>(null)

  // A ref, not state: the auto-start effect must not re-run when it flips, or
  // opening this modal could queue a second pass for the same application.
  const autoStarted = useRef(false)

  const running = current?.status === 'Running'

  const start = async () => {
    setStarting(true)
    setError(null)
    try {
      const started = await api.startPrep(application.id)
      setCurrent(started)
      onRunChange(started)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Could not start prep.')
    } finally {
      setStarting(false)
    }
  }

  useEffect(() => {
    if (!autoStart || autoStarted.current || run?.status === 'Running') return
    autoStarted.current = true
    void start()
    // Deliberately runs at most once per mount — start() is stable in effect.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoStart])

  // Poll while a pass is in flight. Each pass is several chained model calls,
  // so there's no response to wait on — the run row is the progress.
  useEffect(() => {
    if (!running) return

    let cancelled = false
    const timer = setInterval(async () => {
      try {
        const latest = await api.getPrepRun(application.id)
        if (cancelled) return

        if (latest.status !== 'Running') {
          // Load the documents the pipeline wrote onto the application BEFORE
          // publishing the finished run: doing it after would tear this poller
          // down mid-flight, and the fetch would be discarded as cancelled.
          const refreshed = await api.getApplication(application.id)
          if (cancelled) return
          setResumeText(refreshed.tailoredResumeText ?? '')
          setCoverLetterText(refreshed.coverLetterText ?? '')
        }

        setCurrent(latest)
        onRunChange(latest)
      } catch {
        // A blip between polls isn't worth surfacing — the next tick retries,
        // and a genuinely dead server shows up everywhere else in the app.
      }
    }, POLL_INTERVAL_MS)

    return () => {
      cancelled = true
      clearInterval(timer)
    }
  }, [running, application.id, onRunChange])

  const saveDocuments = async () => {
    setSaving(true)
    try {
      await api.saveDocuments(application.id, {
        tailoredResumeText: resumeText.trim() || null,
        coverLetterText: coverLetterText.trim() || null,
      })
      setSavedAt(Date.now())
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Could not save your edits.')
    } finally {
      setSaving(false)
    }
  }

  const hasDocuments = resumeText.trim().length > 0 || coverLetterText.trim().length > 0

  return (
    <ModalBackdrop onClose={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Application prep"
        className="w-full max-w-3xl overflow-hidden rounded-2xl bg-white shadow-2xl"
      >
        <ModalHeader
          title="⚡ Prep to apply"
          subtitle={`${application.companyName} — ${application.roleTitle}`}
          onClose={onClose}
        />

        <div className="max-h-[72vh] space-y-6 overflow-y-auto p-7">
          {error && (
            <p className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700">
              {error}
            </p>
          )}

          {!current && (
            <div className="rounded-2xl border-2 border-dashed border-slate-300 p-7 text-center">
              <p className="text-base font-semibold text-slate-800">Nothing prepped for this role yet</p>
              <p className="mx-auto mt-2 max-w-md text-sm text-slate-500">
                This rewrites your active resume against the posting, re-scores it until it clears{' '}
                the target, and writes a matching cover letter. Takes a couple of minutes.
              </p>
              <button
                type="button"
                onClick={() => void start()}
                disabled={starting}
                className="brand-gradient mt-5 rounded-xl px-5 py-2.5 text-base font-semibold text-white shadow-lg shadow-brand-600/25 transition hover:opacity-95 disabled:opacity-60"
              >
                {starting ? 'Starting…' : 'Run prep'}
              </button>
            </div>
          )}

          {current && (
            <>
              {current.status === 'Succeeded' && current.readyToApply && (
                <div className="rounded-2xl border-2 border-emerald-300 bg-emerald-50 px-5 py-4">
                  <p className="text-lg font-bold text-emerald-900">✓ You're good to apply</p>
                  <p className="mt-1 text-sm text-emerald-800">
                    {current.iterations === 0
                      ? `Your resume already scores ${current.finalScore} against this posting, so there was nothing worth rewriting.`
                      : `Your tailored resume scores ${current.finalScore} against this posting, up from ${current.baselineScore}.`}{' '}
                    Copy what you need below, submit the application, then mark it applied — or just apply
                    and let the Gmail scan catch the confirmation.
                  </p>
                </div>
              )}

              {current.status === 'Succeeded' && !current.readyToApply && (
                <div className="rounded-2xl border-2 border-amber-300 bg-amber-50 px-5 py-4">
                  <p className="text-lg font-bold text-amber-900">
                    Got to {current.finalScore}, short of {current.targetScore}
                  </p>
                  <p className="mt-1 text-sm text-amber-800">
                    Rewriting can only reframe experience you already have. The gaps below are real ones —
                    worth a look before deciding whether this role is worth applying to.
                  </p>
                </div>
              )}

              {current.status === 'Failed' && (
                <div className="rounded-2xl border-2 border-rose-300 bg-rose-50 px-5 py-4">
                  <p className="text-lg font-bold text-rose-900">Prep didn't finish</p>
                  <p className="mt-1 text-sm text-rose-800">{current.errorMessage}</p>
                </div>
              )}

              <div>
                <div className="flex items-center justify-between gap-3">
                  <h3 className="text-sm font-bold tracking-wide text-slate-500 uppercase">Progress</h3>
                  <span className="text-sm text-slate-500">target {current.targetScore}</span>
                </div>
                <ol className="mt-3 space-y-2">
                  {current.steps.map((step, i) => (
                    <li
                      key={i}
                      className="flex items-start gap-3 rounded-xl border border-slate-200 bg-white p-3.5"
                    >
                      <span className="mt-0.5 shrink-0 text-emerald-600" aria-hidden>
                        ✓
                      </span>
                      <div className="min-w-0 flex-1">
                        <p className="font-semibold text-slate-900">{step.label}</p>
                        <p className="mt-0.5 text-sm text-slate-500">{step.detail}</p>
                      </div>
                      {step.score !== null && <ScorePill score={step.score} target={current.targetScore} />}
                    </li>
                  ))}
                  {running && (
                    <li className="flex items-center gap-3 rounded-xl border border-brand-200 bg-brand-50/50 p-3.5">
                      <span className="size-2 shrink-0 animate-pulse rounded-full bg-brand-500" aria-hidden />
                      <p className="text-sm font-medium text-brand-800">
                        {current.steps.length === 0
                          ? 'Scoring your resume against the posting…'
                          : 'Working on the next step…'}
                      </p>
                    </li>
                  )}
                </ol>
              </div>

              {hasDocuments && (
                <div className="space-y-5 border-t border-slate-100 pt-6">
                  {resumeText && (
                    <DocumentPanel
                      title="Tailored resume"
                      value={resumeText}
                      onChange={(next) => {
                        setResumeText(next)
                        setSavedAt(null)
                      }}
                      rows={14}
                    />
                  )}
                  {coverLetterText && (
                    <DocumentPanel
                      title="Cover letter"
                      value={coverLetterText}
                      onChange={(next) => {
                        setCoverLetterText(next)
                        setSavedAt(null)
                      }}
                      rows={10}
                    />
                  )}
                </div>
              )}
            </>
          )}
        </div>

        {current && current.status !== 'Running' && (
          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 bg-slate-50 px-7 py-5">
            <div className="flex items-center gap-3">
              <button
                type="button"
                onClick={() => void start()}
                disabled={starting}
                className="rounded-xl px-4 py-2.5 text-base font-semibold text-slate-600 transition hover:bg-slate-200/60 disabled:opacity-60"
              >
                {starting ? 'Starting…' : 'Run again'}
              </button>
              {hasDocuments && (
                <button
                  type="button"
                  onClick={() => void saveDocuments()}
                  disabled={saving}
                  className="rounded-xl px-4 py-2.5 text-base font-semibold text-slate-600 transition hover:bg-slate-200/60 disabled:opacity-60"
                >
                  {saving ? 'Saving…' : savedAt ? 'Saved ✓' : 'Save edits'}
                </button>
              )}
            </div>

            {application.status === 'Preparing' && (
              <button
                type="button"
                onClick={onMarkApplied}
                className="brand-gradient rounded-xl px-6 py-2.5 text-base font-semibold text-white shadow-lg shadow-brand-600/25 transition hover:opacity-95"
              >
                I applied →
              </button>
            )}
          </div>
        )}
      </div>
    </ModalBackdrop>
  )
}
