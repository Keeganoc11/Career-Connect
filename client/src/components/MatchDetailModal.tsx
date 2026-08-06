import { useState } from 'react'
import type { Application, MatchResult, SuggestedEdit } from '../api/types'
import { scoreBand } from '../lib/matchScore'
import { formatRelative } from '../lib/format'
import { useEscapeKey } from '../lib/useEscapeKey'

interface Props {
  application: Application
  match: MatchResult
  rescoring: boolean
  onRescore: () => void
  onClose: () => void
}

function KeywordList({
  title,
  hint,
  keywords,
  tone,
}: {
  title: string
  hint: string
  keywords: string[]
  tone: 'matched' | 'missing'
}) {
  if (keywords.length === 0) return null

  const chip =
    tone === 'matched'
      ? 'bg-emerald-50 text-emerald-800 ring-emerald-600/25'
      : 'bg-rose-50 text-rose-800 ring-rose-600/25'

  return (
    <div>
      <h3 className="flex items-center gap-2 text-lg font-bold text-slate-900">
        {title}
        <span className="rounded-full bg-slate-100 px-2.5 py-0.5 text-sm font-semibold text-slate-600">
          {keywords.length}
        </span>
      </h3>
      <p className="mt-1 text-sm text-slate-500">{hint}</p>
      <ul className="mt-3 flex flex-wrap gap-2">
        {keywords.map((keyword) => (
          <li
            key={keyword}
            className={`rounded-full px-3 py-1.5 text-sm font-semibold ring-1 ring-inset ${chip}`}
          >
            {keyword}
          </li>
        ))}
      </ul>
    </div>
  )
}

function SuggestionCard({ suggestion }: { suggestion: SuggestedEdit }) {
  const [copied, setCopied] = useState(false)
  const hasText = suggestion.suggestedText.trim().length > 0
  const hasPlaceholder = suggestion.suggestedText.includes('[')

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(suggestion.suggestedText)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard access can be denied by the browser; the text is still
      // selectable and visible, so this is a silent no-op rather than an error.
    }
  }

  return (
    <div className="rounded-xl border border-brand-100 bg-brand-50/40 p-4">
      <span className="inline-block rounded-full bg-white px-2.5 py-1 text-xs font-bold uppercase tracking-wide text-brand-700 ring-1 ring-inset ring-brand-200">
        {suggestion.section}
      </span>
      <p className="mt-2.5 text-sm leading-relaxed text-slate-600">{suggestion.guidance}</p>

      {hasText && (
        <div className="mt-3 flex items-start gap-3 rounded-lg border border-slate-200 bg-white p-3.5">
          <p className="flex-1 text-sm leading-relaxed whitespace-pre-wrap text-slate-800">
            {suggestion.suggestedText}
          </p>
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
      )}
      {hasPlaceholder && (
        <p className="mt-1.5 text-xs text-slate-400">
          Fill in the bracketed [placeholders] with your own specifics before pasting.
        </p>
      )}
    </div>
  )
}

export function MatchDetailModal({ application, match, rescoring, onRescore, onClose }: Props) {
  const band = scoreBand(match.score)
  useEscapeKey(onClose)

  return (
    <div
      className="fixed inset-0 z-30 flex items-start justify-center overflow-y-auto bg-slate-900/50 p-4 pt-10 backdrop-blur-sm"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Match details"
        className="w-full max-w-3xl overflow-hidden rounded-2xl bg-white shadow-2xl"
      >
        <div className="brand-gradient flex items-start justify-between gap-4 px-7 py-5">
          <div>
            <h2 className="text-xl font-bold text-white">
              {application.companyName} — {application.roleTitle}
            </h2>
            <p className="mt-0.5 text-sm text-white/80">
              Scored against “{match.resumeLabel}” · {formatRelative(match.createdAtUtc)}
            </p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="rounded-lg p-1.5 text-white/80 transition hover:bg-white/20 hover:text-white"
          >
            ✕
          </button>
        </div>

        <div className="p-7">
          <div className="flex flex-wrap items-center gap-6 rounded-2xl border border-slate-200 bg-slate-50 p-6">
            <div className="text-center">
              <div className={`text-6xl font-bold tabular-nums ${band.text}`}>{match.score}</div>
              <div className="mt-1 text-sm font-semibold text-slate-500">out of 100</div>
            </div>
            <div className="min-w-64 flex-1">
              <span
                className={`inline-block rounded-full px-3 py-1.5 text-sm font-bold ring-1 ring-inset ${band.badge}`}
              >
                {band.label}
              </span>
              <span className={`mt-3 block h-2.5 w-full overflow-hidden rounded-full ${band.track}`}>
                <span
                  className={`block h-full rounded-full ${band.bar}`}
                  style={{ width: `${Math.max(match.score, 2)}%` }}
                />
              </span>
              <p className="mt-4 text-base leading-relaxed text-slate-700">{match.summary}</p>
            </div>
          </div>

          <div className="mt-8 space-y-7">
            <KeywordList
              title="Covered by your resume"
              hint="Requirements from the posting your resume already evidences."
              keywords={match.matchedKeywords}
              tone="matched"
            />
            <KeywordList
              title="Gaps"
              hint="Asked for in the posting, not visible in your resume."
              keywords={match.missingKeywords}
              tone="missing"
            />

            {match.suggestions.length > 0 && (
              <div>
                <h3 className="text-lg font-bold text-slate-900">Suggested edits</h3>
                <p className="mt-1 text-sm text-slate-500">
                  Ready to copy into your resume — review each one before pasting.
                </p>
                <div className="mt-3 space-y-3">
                  {match.suggestions.map((suggestion, index) => (
                    <SuggestionCard key={`${suggestion.section}-${index}`} suggestion={suggestion} />
                  ))}
                </div>
              </div>
            )}
          </div>
        </div>

        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 bg-slate-50 px-7 py-5">
          <p className="max-w-md text-sm text-slate-500">
            Generated by {match.modelId}. A guide, not a verdict — you know your experience best.
          </p>
          <button
            type="button"
            onClick={onRescore}
            disabled={rescoring}
            className="rounded-xl border-2 border-brand-200 bg-white px-5 py-2.5 text-base font-semibold text-brand-700 transition hover:border-brand-300 hover:bg-brand-50 disabled:opacity-60"
          >
            {rescoring ? 'Re-scoring…' : 'Re-score'}
          </button>
        </div>
      </div>
    </div>
  )
}
