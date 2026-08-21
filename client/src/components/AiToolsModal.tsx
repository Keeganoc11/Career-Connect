import { useState } from 'react'
import { api, ApiError } from '../api/client'
import type { Application, InterviewPrep } from '../api/types'
import { ModalBackdrop, ModalHeader } from './Modal'

interface Props {
  application: Application
  onClose: () => void
}

function CopyButton({ text }: { text: string }) {
  const [copied, setCopied] = useState(false)

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard access can be denied by the browser; the text is still
      // selectable and visible, so this is a silent no-op rather than an error.
    }
  }

  return (
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
  )
}

function CoverLetterSection({ applicationId }: { applicationId: string }) {
  const [content, setContent] = useState<string | null>(null)
  const [generating, setGenerating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const generate = async () => {
    setGenerating(true)
    setError(null)
    try {
      const result = await api.generateCoverLetter(applicationId)
      setContent(result.content)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Something went wrong.')
    } finally {
      setGenerating(false)
    }
  }

  return (
    <div>
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-lg font-bold text-slate-900">Cover letter</h3>
        <button
          type="button"
          onClick={() => void generate()}
          disabled={generating}
          className="brand-gradient rounded-lg px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:opacity-95 disabled:opacity-60"
        >
          {generating ? 'Writing…' : content ? 'Regenerate' : 'Generate'}
        </button>
      </div>

      {error && (
        <p className="mt-3 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700">
          {error}
        </p>
      )}

      {content && (
        <div className="mt-3 flex items-start gap-3 rounded-xl border border-slate-200 bg-white p-4">
          <p className="flex-1 text-sm leading-relaxed whitespace-pre-wrap text-slate-800">{content}</p>
          <CopyButton text={content} />
        </div>
      )}
    </div>
  )
}

function InterviewPrepSection({ applicationId }: { applicationId: string }) {
  const [prep, setPrep] = useState<InterviewPrep | null>(null)
  const [generating, setGenerating] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const generate = async () => {
    setGenerating(true)
    setError(null)
    try {
      setPrep(await api.generateInterviewPrep(applicationId))
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Something went wrong.')
    } finally {
      setGenerating(false)
    }
  }

  return (
    <div>
      <div className="flex items-center justify-between gap-3">
        <h3 className="text-lg font-bold text-slate-900">Interview prep</h3>
        <button
          type="button"
          onClick={() => void generate()}
          disabled={generating}
          className="brand-gradient rounded-lg px-4 py-2 text-sm font-semibold text-white shadow-sm transition hover:opacity-95 disabled:opacity-60"
        >
          {generating ? 'Thinking…' : prep ? 'Regenerate' : 'Generate'}
        </button>
      </div>

      {error && (
        <p className="mt-3 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700">
          {error}
        </p>
      )}

      {prep && (
        <div className="mt-3 space-y-5">
          <div>
            <h4 className="text-sm font-bold tracking-wide text-slate-500 uppercase">Likely questions</h4>
            <div className="mt-2 space-y-2">
              {prep.questions.map((q, i) => (
                <div key={i} className="rounded-xl border border-slate-200 bg-white p-3.5">
                  <p className="font-semibold text-slate-900">{q.question}</p>
                  <p className="mt-1 text-sm text-slate-500">{q.whyItMightComeUp}</p>
                </div>
              ))}
            </div>
          </div>
          <div>
            <h4 className="text-sm font-bold tracking-wide text-slate-500 uppercase">Talking points</h4>
            <div className="mt-2 space-y-2">
              {prep.talkingPoints.map((t, i) => (
                <div key={i} className="rounded-xl border border-brand-100 bg-brand-50/40 p-3.5">
                  <p className="font-semibold text-slate-900">{t.point}</p>
                  <p className="mt-1 text-sm text-slate-600">{t.howToUseIt}</p>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  )
}

export function AiToolsModal({ application, onClose }: Props) {
  const showInterviewPrep = application.status === 'Interview' || application.status === 'Offer'

  return (
    <ModalBackdrop onClose={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        aria-label="AI tools"
        className="w-full max-w-2xl overflow-hidden rounded-2xl bg-white shadow-2xl"
      >
        <ModalHeader
          title="✨ AI tools"
          subtitle={`${application.companyName} — ${application.roleTitle}`}
          onClose={onClose}
        />

        <div className="max-h-[70vh] space-y-7 overflow-y-auto p-7">
          <CoverLetterSection applicationId={application.id} />
          {showInterviewPrep && <InterviewPrepSection applicationId={application.id} />}
        </div>
      </div>
    </ModalBackdrop>
  )
}
