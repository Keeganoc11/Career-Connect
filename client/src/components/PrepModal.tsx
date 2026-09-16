import { useEffect, useRef, useState } from 'react'
import { Check, Sparkles } from 'lucide-react'
import { api } from '../api/client'
import type { Application, PrepRun } from '../api/types'
import { useAsyncAction } from '../lib/useAsyncAction'
import {
  Badge,
  Banner,
  Button,
  ConfirmDialog,
  CopyButton,
  EmptyState,
  Modal,
  Spinner,
  Textarea,
  toast,
} from './ui'

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
  return (
    <div>
      <div className="mb-2 flex items-center justify-between gap-3">
        <h3 className="text-sm font-semibold text-fg">{title}</h3>
        <CopyButton text={value} />
      </div>
      <Textarea monospace rows={rows} value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}

export function PrepModal({
  application,
  run,
  autoStart,
  onRunChange,
  onMarkApplied,
  onClose,
}: Props) {
  const [current, setCurrent] = useState<PrepRun | null>(run)
  const [resumeText, setResumeText] = useState(application.tailoredResumeText ?? '')
  const [coverLetterText, setCoverLetterText] = useState(application.coverLetterText ?? '')
  const [savedEdits, setSavedEdits] = useState(false)
  const [confirmingRerun, setConfirmingRerun] = useState(false)

  const start = useAsyncAction()
  const save = useAsyncAction()

  // Set once the user types into either document. The fresh load below must
  // never replace text they're already editing.
  const editedRef = useRef(false)

  // The prop is only as current as the page's last load, and the documents can
  // change elsewhere in the meantime (a cover letter written from its own
  // window). Seeding from it alone would let "Save edits" write a stale copy
  // back over the newer one, so load the saved documents on open.
  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const fresh = await api.getApplication(application.id)
        if (cancelled || editedRef.current) return
        setResumeText(fresh.tailoredResumeText ?? '')
        setCoverLetterText(fresh.coverLetterText ?? '')
      } catch {
        // Keep the seeded copy; a server that's down shows up across the page.
      }
    })()
    return () => {
      cancelled = true
    }
  }, [application.id])

  // A ref, not state: the auto-start effect must not re-run when it flips, or
  // opening this modal could queue a second pass for the same application.
  const autoStarted = useRef(false)
  const running = current?.status === 'Running'

  const startPrep = () =>
    start.run(async () => {
      const started = await api.startPrep(application.id)
      setCurrent(started)
      onRunChange(started)
    })

  useEffect(() => {
    if (!autoStart || autoStarted.current || run?.status === 'Running') return
    autoStarted.current = true
    void startPrep()
    // Deliberately runs at most once per mount — startPrep is stable in effect.
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
          if (!editedRef.current) {
            setResumeText(refreshed.tailoredResumeText ?? '')
            setCoverLetterText(refreshed.coverLetterText ?? '')
          }
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

  const saveDocuments = () =>
    void save.run(async () => {
      await api.saveDocuments(application.id, {
        tailoredResumeText: resumeText.trim() || null,
        coverLetterText: coverLetterText.trim() || null,
      })
      editedRef.current = false
      setSavedEdits(true)
      toast.success('Edits saved.')
    })

  const hasDocuments = resumeText.trim().length > 0 || coverLetterText.trim().length > 0
  const edited = editedRef.current && !savedEdits

  const rerun = () => {
    if (hasDocuments) {
      setConfirmingRerun(true)
      return
    }
    void startPrep()
  }

  return (
    <>
      <Modal
        title="Application prep"
        description={`${application.companyName} · ${application.roleTitle}`}
        size="lg"
        error={start.error ?? save.error}
        busy={save.busy}
        dirty={edited}
        onClose={onClose}
        footerStart={
          current &&
          !running && (
            <>
              <Button
                icon={<Sparkles className="size-4" aria-hidden />}
                loading={start.busy}
                onClick={rerun}
              >
                Run prep again
              </Button>
              {edited && (
                <Button loading={save.busy} onClick={saveDocuments}>
                  Save edits
                </Button>
              )}
            </>
          )
        }
        footer={
          application.status === 'Preparing' && current && !running ? (
            <Button variant="primary" onClick={onMarkApplied}>
              Mark as applied
            </Button>
          ) : (
            <Button onClick={onClose}>Close</Button>
          )
        }
      >
        <div className="space-y-5">
          {!current && !start.busy && (
            <EmptyState
              title="Nothing prepped for this role yet"
              description="This rewrites your active resume against the posting, re-scores it until it clears the target, and writes a matching cover letter. Takes a couple of minutes."
              action={
                <Button
                  variant="primary"
                  icon={<Sparkles className="size-4" aria-hidden />}
                  loading={start.busy}
                  onClick={() => void startPrep()}
                >
                  Run prep
                </Button>
              }
            />
          )}

          {current && (
            <>
              {current.status === 'Succeeded' && current.readyToApply && (
                <Banner tone="success">
                  {current.iterations === 0
                    ? `Your resume already scores ${current.finalScore} against this posting, so there was nothing worth rewriting.`
                    : `Your tailored resume scores ${current.finalScore} against this posting, up from ${current.baselineScore}.`}{' '}
                  Copy what you need below, then mark it applied — or just apply and let the email
                  scan catch the confirmation.
                </Banner>
              )}

              {current.status === 'Succeeded' && !current.readyToApply && (
                <Banner tone="warning">
                  Got to {current.finalScore}, short of {current.targetScore}. Rewriting can only
                  reframe experience you already have, so the gaps are real ones — worth a look
                  before deciding whether to apply.
                </Banner>
              )}

              {current.status === 'Failed' && <Banner>{current.errorMessage}</Banner>}

              <section>
                <div className="mb-2 flex items-center justify-between gap-3">
                  <h3 className="text-sm font-semibold text-fg">Progress</h3>
                  <span className="text-xs text-fg-muted tabular-nums">
                    target {current.targetScore}
                  </span>
                </div>
                <ol className="space-y-2">
                  {current.steps.map((step, index) => (
                    <li
                      key={index}
                      className="flex items-start gap-2.5 rounded-control border border-line px-3.5 py-3"
                    >
                      <Check className="mt-0.5 size-4 shrink-0 text-success" aria-hidden />
                      <div className="min-w-0 flex-1">
                        <p className="text-sm font-medium text-fg">{step.label}</p>
                        <p className="mt-0.5 text-sm text-fg-muted">{step.detail}</p>
                      </div>
                      {step.score !== null && (
                        <Badge
                          emphasis={step.score >= current.targetScore ? 'accent' : 'neutral'}
                        >
                          <span className="tabular-nums">{step.score}</span>
                        </Badge>
                      )}
                    </li>
                  ))}
                  {running && (
                    <li className="flex items-center gap-2.5 rounded-control border border-line px-3.5 py-3">
                      <Spinner />
                      <p className="text-sm text-fg-muted">
                        {current.steps.length === 0
                          ? 'Scoring your resume against the posting…'
                          : 'Working on the next step…'}
                      </p>
                    </li>
                  )}
                </ol>
                {running && (
                  <p className="mt-2 text-xs text-fg-muted">
                    Prep keeps running if you close this — the row updates when it finishes.
                  </p>
                )}
              </section>

              {hasDocuments && (
                <div className="space-y-5 border-t border-line pt-5">
                  {resumeText && (
                    <DocumentPanel
                      title="Tailored resume"
                      value={resumeText}
                      onChange={(next) => {
                        editedRef.current = true
                        setResumeText(next)
                        setSavedEdits(false)
                      }}
                      rows={14}
                    />
                  )}
                  {coverLetterText && (
                    <DocumentPanel
                      title="Cover letter"
                      value={coverLetterText}
                      onChange={(next) => {
                        editedRef.current = true
                        setCoverLetterText(next)
                        setSavedEdits(false)
                      }}
                      rows={10}
                    />
                  )}
                </div>
              )}
            </>
          )}
        </div>
      </Modal>

      {confirmingRerun && (
        <ConfirmDialog
          title="Run prep again?"
          body="The tailored resume and cover letter below are replaced by a fresh pass, and any edits to them are lost."
          confirmLabel="Run prep again"
          busyLabel="Starting…"
          busy={start.busy}
          error={start.error}
          onCancel={() => setConfirmingRerun(false)}
          onConfirm={() => {
            void startPrep().then((ok) => {
              if (ok) {
                editedRef.current = false
                setConfirmingRerun(false)
              }
            })
          }}
        />
      )}
    </>
  )
}
