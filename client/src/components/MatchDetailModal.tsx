import { useCallback, useState } from 'react'
import type { Application, MatchResult, Resume, ResumeSummary, SuggestedEdit, TailorResumeInput } from '../api/types'
import { scoreBand } from '../lib/matchScore'
import { formatRelative } from '../lib/format'
import { fieldClass } from '../lib/styles'
import { ModalBackdrop, ModalHeader } from './Modal'

const MIN_RESUME_LENGTH = 50

interface Props {
  application: Application
  match: MatchResult
  resumes: ResumeSummary[]
  rescoring: boolean
  tailoring: boolean
  onRescore: () => void
  onLoadResumeContent: (id: string) => Promise<Resume>
  onTailorAndRescore: (input: TailorResumeInput) => Promise<void>
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

function TailorPanel({
  application,
  match,
  resumes,
  tailoring,
  onLoadResumeContent,
  onTailorAndRescore,
}: {
  application: Application
  match: MatchResult
  resumes: ResumeSummary[]
  tailoring: boolean
  onLoadResumeContent: (id: string) => Promise<Resume>
  onTailorAndRescore: (input: TailorResumeInput) => Promise<void>
}) {
  const [expanded, setExpanded] = useState(false)
  const [selection, setSelection] = useState<string>(() =>
    resumes.some((r) => r.id === match.resumeId) ? match.resumeId : 'new',
  )
  const [label, setLabel] = useState('')
  const [content, setContent] = useState('')
  const [loadingContent, setLoadingContent] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [justSaved, setJustSaved] = useState(false)

  const loadSelection = useCallback(
    async (value: string) => {
      setError(null)
      setLoadingContent(true)
      try {
        if (value === 'new') {
          // Seed a new resume with the text that actually earned this score,
          // so tailoring starts from something instead of a blank page.
          const seed = await onLoadResumeContent(match.resumeId)
          setLabel(`${application.companyName} — tailored`)
          setContent(seed.content)
        } else {
          const resume = await onLoadResumeContent(value)
          setLabel(resume.label)
          setContent(resume.content)
        }
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Could not load resume.')
      } finally {
        setLoadingContent(false)
      }
    },
    [application.companyName, match.resumeId, onLoadResumeContent],
  )

  const expand = () => {
    setExpanded(true)
    void loadSelection(selection)
  }

  const changeSelection = (value: string) => {
    setSelection(value)
    void loadSelection(value)
  }

  const submit = async () => {
    setError(null)
    try {
      await onTailorAndRescore(
        selection === 'new'
          ? { mode: 'new', label: label.trim(), content: content.trim() }
          : { mode: 'existing', resumeId: selection, label: label.trim(), content: content.trim() },
      )
      setJustSaved(true)
      setTimeout(() => setJustSaved(false), 3000)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save and re-score.')
    }
  }

  if (!expanded) {
    return (
      <button
        type="button"
        onClick={expand}
        className="text-sm font-bold text-brand-600 transition hover:text-brand-700"
      >
        Tailor this resume →
      </button>
    )
  }

  const tooShort = content.trim().length > 0 && content.trim().length < MIN_RESUME_LENGTH
  const canSubmit =
    !tailoring &&
    !loadingContent &&
    content.trim().length >= MIN_RESUME_LENGTH &&
    (selection !== 'new' || label.trim().length > 0)

  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-5">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <h3 className="text-lg font-bold text-slate-900">Tailor &amp; re-score</h3>
        {justSaved && (
          <span className="rounded-full bg-emerald-50 px-3 py-1 text-sm font-semibold text-emerald-700 ring-1 ring-inset ring-emerald-600/20">
            Saved and re-scored ✓
          </span>
        )}
      </div>
      <p className="mt-1 text-sm text-slate-500">
        Edit the resume below, then save — it becomes your active resume and this application is
        re-scored against it immediately.
      </p>

      <label className="mt-4 block">
        <span className="mb-1.5 block text-sm font-semibold text-slate-700">Editing</span>
        <select
          className={fieldClass}
          value={selection}
          onChange={(e) => changeSelection(e.target.value)}
          disabled={loadingContent}
        >
          {resumes.map((r) => (
            <option key={r.id} value={r.id}>
              {r.label}
              {r.isActive ? ' (active)' : ''}
            </option>
          ))}
          <option value="new">+ New resume for this application</option>
        </select>
      </label>

      {selection === 'new' && (
        <label className="mt-4 block">
          <span className="mb-1.5 block text-sm font-semibold text-slate-700">Label</span>
          <input
            className={fieldClass}
            value={label}
            onChange={(e) => setLabel(e.target.value)}
            maxLength={200}
          />
        </label>
      )}

      <label className="mt-4 block">
        <span className="mb-1.5 block text-sm font-semibold text-slate-700">Resume text</span>
        <textarea
          className={`${fieldClass} font-mono text-sm leading-relaxed`}
          rows={14}
          value={content}
          onChange={(e) => setContent(e.target.value)}
          disabled={loadingContent}
          placeholder={loadingContent ? 'Loading…' : undefined}
        />
        {tooShort && (
          <span className="mt-1 block text-xs font-medium text-rose-600">
            At least {MIN_RESUME_LENGTH} characters needed.
          </span>
        )}
      </label>

      {error && (
        <p className="mt-3 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700">
          {error}
        </p>
      )}

      <div className="mt-4 flex justify-end gap-2">
        <button
          type="button"
          onClick={() => setExpanded(false)}
          className="rounded-lg px-4 py-2 text-sm font-semibold text-slate-500 transition hover:bg-slate-100"
        >
          Cancel
        </button>
        <button
          type="button"
          onClick={() => void submit()}
          disabled={!canSubmit}
          className="brand-gradient rounded-lg px-5 py-2 text-sm font-semibold text-white shadow-sm transition hover:opacity-95 disabled:opacity-60"
        >
          {tailoring ? 'Saving & scoring…' : 'Save & re-score'}
        </button>
      </div>
    </div>
  )
}

export function MatchDetailModal({
  application,
  match,
  resumes,
  rescoring,
  tailoring,
  onRescore,
  onLoadResumeContent,
  onTailorAndRescore,
  onClose,
}: Props) {
  const band = scoreBand(match.score)

  return (
    <ModalBackdrop onClose={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label="Match details"
        className="w-full max-w-3xl overflow-hidden rounded-2xl bg-white shadow-2xl"
      >
        <ModalHeader
          title={`${application.companyName} — ${application.roleTitle}`}
          subtitle={`Scored against “${match.resumeLabel}” · ${formatRelative(match.createdAtUtc)}`}
          onClose={onClose}
        />

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

            <TailorPanel
              application={application}
              match={match}
              resumes={resumes}
              tailoring={tailoring}
              onLoadResumeContent={onLoadResumeContent}
              onTailorAndRescore={onTailorAndRescore}
            />
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
    </ModalBackdrop>
  )
}
