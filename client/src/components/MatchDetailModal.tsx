import { useCallback, useState } from 'react'
import { Sparkles, Undo2 } from 'lucide-react'
import { api } from '../api/client'
import type {
  Application,
  MatchResult,
  Resume,
  ResumeSummary,
  SuggestedEdit,
  TailorResumeInput,
} from '../api/types'
import { scoreBand } from '../lib/matchScore'
import { formatRelative } from '../lib/format'
import { useAsyncAction } from '../lib/useAsyncAction'
import {
  Badge,
  Banner,
  Button,
  Card,
  ConfirmDialog,
  CopyButton,
  Field,
  Input,
  Modal,
  Select,
  Textarea,
  toast,
} from './ui'

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

function KeywordList({ title, hint, keywords }: { title: string; hint: string; keywords: string[] }) {
  if (keywords.length === 0) return null

  return (
    <section>
      <h3 className="text-sm font-semibold text-fg">
        {title} <span className="text-fg-muted tabular-nums">({keywords.length})</span>
      </h3>
      <p className="mt-0.5 text-sm text-fg-muted">{hint}</p>
      {/* Neutral chips in both lists: a red "gap" pill reads as an error, and
          feedback colour is reserved for banners and toasts. */}
      <ul className="mt-2 flex flex-wrap gap-1.5">
        {keywords.map((keyword) => (
          <li key={keyword}>
            <Badge>{keyword}</Badge>
          </li>
        ))}
      </ul>
    </section>
  )
}

function SuggestionCard({ suggestion }: { suggestion: SuggestedEdit }) {
  const hasText = suggestion.suggestedText.trim().length > 0
  const hasPlaceholder = suggestion.suggestedText.includes('[')

  return (
    <Card>
      <Badge emphasis="accent">{suggestion.section}</Badge>
      <p className="mt-2 text-sm leading-relaxed text-fg-muted">{suggestion.guidance}</p>

      {hasText && (
        <div className="mt-3 rounded-control border border-line p-3">
          <p className="whitespace-pre-wrap text-sm leading-relaxed text-fg">
            {suggestion.suggestedText}
          </p>
          <div className="mt-2 flex justify-end">
            <CopyButton text={suggestion.suggestedText} />
          </div>
        </div>
      )}
      {hasPlaceholder && (
        <p className="mt-1.5 text-xs text-fg-muted">
          Fill in the bracketed [placeholders] with your own specifics before pasting.
        </p>
      )}
    </Card>
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
  /** The text as it was before a rewrite, so the model's pass can be undone. */
  const [beforeRewrite, setBeforeRewrite] = useState<string | null>(null)
  const [confirmingSave, setConfirmingSave] = useState(false)

  const load = useAsyncAction()
  const rewrite = useAsyncAction()
  const submit = useAsyncAction()

  const loadSelection = useCallback(
    (value: string) =>
      load.run(async () => {
        if (value === 'new') {
          // Seed a new resume with the text that actually earned this score, so
          // tailoring starts from something instead of a blank page.
          const seed = await onLoadResumeContent(match.resumeId)
          setLabel(`${application.companyName} — tailored`)
          setContent(seed.content)
        } else {
          const resume = await onLoadResumeContent(value)
          setLabel(resume.label)
          setContent(resume.content)
        }
        setBeforeRewrite(null)
      }),
    // load.run is stable; including the action object would re-create this hook.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [application.companyName, match.resumeId, onLoadResumeContent],
  )

  const changeSelection = (value: string) => {
    setSelection(value)
    void loadSelection(value)
  }

  /**
   * Undo rather than a confirmation: the rewrite is reversible in one click,
   * and asking first would stand between the user and the thing they came for.
   */
  const runRewrite = () =>
    void rewrite.run(async () => {
      const previous = content
      const result = await api.tailorResume(application.id, content)
      setBeforeRewrite(previous)
      setContent(result.content)
    })

  const save = () =>
    void submit.run(async () => {
      await onTailorAndRescore(
        selection === 'new'
          ? { mode: 'new', label: label.trim(), content: content.trim() }
          : { mode: 'existing', resumeId: selection, label: label.trim(), content: content.trim() },
      )
      setConfirmingSave(false)
      toast.success('Saved and re-scored.')
    })

  if (!expanded) {
    return (
      <Button
        onClick={() => {
          setExpanded(true)
          void loadSelection(selection)
        }}
      >
        Tailor this resume
      </Button>
    )
  }

  const tooShort = content.trim().length > 0 && content.trim().length < MIN_RESUME_LENGTH
  const canSubmit =
    !tailoring &&
    !load.busy &&
    content.trim().length >= MIN_RESUME_LENGTH &&
    (selection !== 'new' || label.trim().length > 0)

  const overwritingExisting = selection !== 'new'

  return (
    <>
      <Card>
        <h3 className="text-sm font-semibold text-fg">Tailor &amp; re-score</h3>
        <p className="mt-0.5 text-sm text-fg-muted">
          Edit the resume below, then save — it becomes your active resume and this application is
          re-scored against it straight away.
        </p>

        <div className="mt-4 space-y-4">
          <Field label="Editing">
            {(props) => (
              <Select
                {...props}
                value={selection}
                onChange={(e) => changeSelection(e.target.value)}
                disabled={load.busy}
              >
                {resumes.map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.label}
                    {r.isActive ? ' (active)' : ''}
                  </option>
                ))}
                <option value="new">New resume for this application</option>
              </Select>
            )}
          </Field>

          {selection === 'new' && (
            <Field label="Label">
              {(props) => (
                <Input
                  {...props}
                  value={label}
                  onChange={(e) => setLabel(e.target.value)}
                  maxLength={200}
                />
              )}
            </Field>
          )}

          <Field
            label="Resume text"
            error={tooShort ? `At least ${MIN_RESUME_LENGTH} characters needed.` : rewrite.error}
            hint="Reorders and reframes the experience you already have to fit this posting — review it before saving."
          >
            {(props) => (
              <>
                <div className="mb-1.5 flex justify-end gap-2">
                  {beforeRewrite !== null && (
                    <Button
                      size="sm"
                      icon={<Undo2 className="size-4" aria-hidden />}
                      onClick={() => {
                        setContent(beforeRewrite)
                        setBeforeRewrite(null)
                      }}
                    >
                      Undo
                    </Button>
                  )}
                  <Button
                    size="sm"
                    icon={<Sparkles className="size-4" aria-hidden />}
                    loading={rewrite.busy}
                    disabled={load.busy || content.trim().length < MIN_RESUME_LENGTH}
                    onClick={runRewrite}
                  >
                    Rewrite for this job
                  </Button>
                </div>
                <Textarea
                  {...props}
                  monospace
                  rows={14}
                  value={content}
                  onChange={(e) => {
                    setContent(e.target.value)
                    setBeforeRewrite(null)
                  }}
                  disabled={load.busy}
                  placeholder={load.busy ? 'Loading…' : undefined}
                />
              </>
            )}
          </Field>

          {(load.error || submit.error) && <Banner>{load.error ?? submit.error}</Banner>}

          <div className="flex justify-end gap-2">
            <Button onClick={() => setExpanded(false)}>Cancel</Button>
            <Button
              variant="primary"
              loading={tailoring || submit.busy}
              disabled={!canSubmit}
              onClick={() => (overwritingExisting ? setConfirmingSave(true) : save())}
            >
              Save &amp; re-score
            </Button>
          </div>
        </div>
      </Card>

      {confirmingSave && (
        <ConfirmDialog
          title="Save over this resume?"
          body={`This replaces the saved text of “${label}” and makes it your active resume, which is what future match scores are calculated against.`}
          confirmLabel="Save & re-score"
          busyLabel="Saving…"
          busy={tailoring || submit.busy}
          error={submit.error}
          onCancel={() => setConfirmingSave(false)}
          onConfirm={save}
        />
      )}
    </>
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
    <Modal
      title="Match score"
      description={`${application.companyName} · ${application.roleTitle}`}
      size="lg"
      onClose={onClose}
      footerStart={
        <span className="text-xs text-fg-muted">A guide, not a verdict.</span>
      }
      footer={
        <>
          <Button loading={rescoring} onClick={onRescore}>
            Re-score
          </Button>
          <Button onClick={onClose}>Close</Button>
        </>
      }
    >
      <div className="space-y-6">
        <Card>
          <div className="flex flex-wrap items-center gap-5">
            <div className="text-center">
              <div className={`text-5xl font-semibold tabular-nums ${band.text}`}>
                {match.score}
              </div>
              <div className="mt-0.5 text-xs text-fg-muted">out of 100</div>
            </div>
            <div className="min-w-56 flex-1">
              <Badge>{band.label}</Badge>
              <span className={`mt-2 block h-2 w-full overflow-hidden rounded-full ${band.track}`}>
                <span
                  className={`block h-full rounded-full ${band.bar}`}
                  style={{ width: `${Math.max(match.score, 2)}%` }}
                />
              </span>
              <p className="mt-3 text-sm leading-relaxed text-fg">{match.summary}</p>
            </div>
          </div>
          <p className="mt-3 text-xs text-fg-muted">
            Scored against “{match.resumeLabel}” · {formatRelative(match.createdAtUtc)}
          </p>
        </Card>

        <KeywordList
          title="Covered by your resume"
          hint="Requirements from the posting your resume already evidences."
          keywords={match.matchedKeywords}
        />
        <KeywordList
          title="Gaps"
          hint="Asked for in the posting, not visible in your resume."
          keywords={match.missingKeywords}
        />

        {match.suggestions.length > 0 && (
          <section>
            <h3 className="text-sm font-semibold text-fg">Suggested edits</h3>
            <p className="mt-0.5 text-sm text-fg-muted">
              Ready to copy into your resume — review each one before pasting.
            </p>
            <div className="mt-2 space-y-3">
              {match.suggestions.map((suggestion, index) => (
                <SuggestionCard key={`${suggestion.section}-${index}`} suggestion={suggestion} />
              ))}
            </div>
          </section>
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
    </Modal>
  )
}
