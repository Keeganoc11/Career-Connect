import { useCallback, useEffect, useState } from 'react'
import { ArrowLeft, Sparkles } from 'lucide-react'
import { api, ApiError } from '../api/client'
import type { InterviewPrepTracker } from '../api/types'
import { InterviewDebriefPanel } from '../components/InterviewDebriefPanel'
import { InterviewQuestionList } from '../components/InterviewQuestionList'
import {
  Badge,
  Banner,
  Button,
  Card,
  EmptyState,
  Field,
  LoadingState,
  Textarea,
  toast,
} from '../components/ui'
import { errorMessage } from '../lib/errors'
import { formatDateTime, formatUntil } from '../lib/format'
import { KIND_LABELS } from '../lib/interviews'
import { useAsyncAction } from '../lib/useAsyncAction'

interface Props {
  interviewId: string
  onBack: () => void
  onOpenJob: (applicationId: string) => void
}

const RATINGS = [1, 2, 3, 4, 5]

/**
 * One interview round, end to end: what you found out beforehand, what was
 * asked, what you asked, how you felt it went, and the debrief scored against
 * the posting.
 *
 * Its own page rather than a dialog, because half of it is filled in before the
 * round and half after — it's somewhere you come back to, and a reload has to
 * land you back in it.
 */
export function InterviewPage({ interviewId, onBack, onOpenJob }: Props) {
  const [prep, setPrep] = useState<InterviewPrepTracker | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [missing, setMissing] = useState(false)

  const [research, setResearch] = useState('')
  const [reflection, setReflection] = useState('')

  const suggest = useAsyncAction()
  const debrief = useAsyncAction()
  const saveResearch = useAsyncAction()
  const saveReflection = useAsyncAction()

  const load = useCallback(async () => {
    try {
      const loaded = await api.getInterviewTracker(interviewId)
      setPrep(loaded)
      setMissing(false)
      setLoadError(null)
    } catch (e) {
      if (e instanceof ApiError && e.status === 404) setMissing(true)
      else setLoadError(errorMessage(e))
    } finally {
      setLoading(false)
    }
  }, [interviewId])

  useEffect(() => {
    void load()
  }, [load])

  // The textareas are uncontrolled by the server after the first load: typing
  // into one while a refetch lands must not replace what's being written.
  const [hydrated, setHydrated] = useState(false)
  useEffect(() => {
    if (!prep || hydrated) return
    setResearch(prep.researchNotes ?? '')
    setReflection(prep.reflection ?? '')
    setHydrated(true)
  }, [prep, hydrated])

  useEffect(() => {
    if (prep) document.title = `${prep.companyName} interview · Career Connect`
  }, [prep])

  if (loading) return <LoadingState label="Loading this interview…" />

  if (missing) {
    return (
      <Card>
        <EmptyState
          title="That interview is gone"
          description="It was deleted, or it belongs to an application that was."
          action={<Button onClick={onBack}>Back</Button>}
        />
      </Card>
    )
  }

  if (!prep) {
    return (
      <Card>
        <EmptyState
          title="Couldn’t load this interview"
          description={loadError ?? undefined}
          action={<Button onClick={() => void load()}>Try again</Button>}
        />
      </Card>
    )
  }

  const past = new Date(prep.scheduledAtUtc).getTime() < Date.now()
  const realQuestions = prep.questions.filter((q) => q.side === 'TheyAsked' && !q.suggested)

  const debriefBlockedReason = !prep.hasJobDescription
    ? 'The debrief is scored against the job description, and this application doesn’t have one yet.'
    : realQuestions.length === 0 && !prep.reflection
      ? 'Log what they asked, or write down how it went, and there’s something to score.'
      : null

  const saveResearchNotes = () => {
    if (research.trim() === (prep.researchNotes ?? '').trim()) return
    void saveResearch.run(async () => {
      setPrep(await api.saveInterviewResearch(interviewId, research.trim()))
    })
  }

  const saveReflectionNotes = (rating = prep.selfRating) => {
    void saveReflection.run(async () => {
      setPrep(await api.saveInterviewReflection(interviewId, reflection.trim(), rating ?? null))
    })
  }

  return (
    <div className="space-y-5">
      <div>
        <Button variant="ghost" icon={<ArrowLeft className="size-4" aria-hidden />} onClick={onBack}>
          Back
        </Button>
      </div>

      <header>
        <h1 className="text-xl font-semibold text-fg">
          {prep.companyName} · {KIND_LABELS[prep.kind]}
        </h1>
        <p className="mt-0.5 text-sm text-fg-muted">
          {prep.roleTitle} ·{' '}
          {/* A button, not TextLink: navigating in-app is what makes Back
              return here rather than to the applications list. */}
          <button
            type="button"
            onClick={() => onOpenJob(prep.applicationId)}
            className="rounded-sm font-medium text-accent underline underline-offset-2 hover:text-accent-hover"
          >
            Open the job
          </button>
        </p>
        <div className="mt-2 flex flex-wrap items-center gap-2">
          <span className="text-sm text-fg">{formatDateTime(prep.scheduledAtUtc)}</span>
          {!past && <Badge emphasis="interview">{formatUntil(prep.scheduledAtUtc)}</Badge>}
        </div>
        {prep.notes && (
          <p className="mt-2 text-sm whitespace-pre-wrap text-fg-muted">{prep.notes}</p>
        )}
      </header>

      {loadError && <Banner>{loadError}</Banner>}

      <Card>
        <h2 className="text-base font-semibold text-fg">Company research</h2>
        <p className="mt-0.5 mb-3 text-sm text-fg-muted">
          What they build, who you’re meeting, recent news — and why you want it. Saved when you
          click away.
        </p>
        <Textarea
          rows={6}
          value={research}
          maxLength={20000}
          onChange={(event) => setResearch(event.target.value)}
          onBlur={saveResearchNotes}
          placeholder="Their product, the team, recent launches, questions this raises…"
          aria-label="Company research"
        />
        {saveResearch.error && <Banner>{saveResearch.error}</Banner>}
      </Card>

      <InterviewQuestionList
        interviewId={interviewId}
        side="TheyAsked"
        questions={prep.questions}
        onChanged={load}
        header={
          <div className="flex flex-wrap items-start justify-between gap-2">
            <div>
              <h2 className="text-base font-semibold text-fg">Questions they asked</h2>
              <p className="mt-0.5 text-sm text-fg-muted">
                Write the answer you gave and how it landed. This is what the debrief reads.
              </p>
            </div>
            <Button
              icon={<Sparkles className="size-4" aria-hidden />}
              loading={suggest.busy}
              disabled={!prep.hasJobDescription}
              onClick={() =>
                void suggest.run(async () => {
                  const added = await api.suggestInterviewQuestions(interviewId)
                  await load()
                  toast.success(
                    added.length === 0
                      ? 'Nothing new to add — you already have those.'
                      : `Added ${added.length} question${added.length === 1 ? '' : 's'} to prepare for.`,
                  )
                })
              }
            >
              Suggest questions
            </Button>
          </div>
        }
      />

      {suggest.error && <Banner>{suggest.error}</Banner>}

      <InterviewQuestionList
        interviewId={interviewId}
        side="YouAsk"
        questions={prep.questions}
        onChanged={load}
        header={
          <div>
            <h2 className="text-base font-semibold text-fg">Questions to ask them</h2>
            <p className="mt-0.5 text-sm text-fg-muted">
              Tick them off as you go, and write down what they said.
            </p>
          </div>
        }
      />

      <Card>
        <h2 className="text-base font-semibold text-fg">How it went</h2>
        <p className="mt-0.5 mb-3 text-sm text-fg-muted">
          Your own read, before anything scores it — write it while it’s fresh.
        </p>

        <Field label="Your rating">
          {() => (
            <div className="flex flex-wrap gap-2">
              {RATINGS.map((rating) => (
                <button
                  key={rating}
                  type="button"
                  aria-pressed={prep.selfRating === rating}
                  aria-label={`${rating} out of 5`}
                  onClick={() => saveReflectionNotes(prep.selfRating === rating ? null : rating)}
                  className={`size-9 rounded-control border text-sm transition-colors ${
                    prep.selfRating === rating
                      ? 'border-accent bg-accent-soft font-medium text-accent'
                      : 'border-line text-fg-muted hover:text-fg'
                  }`}
                >
                  {rating}
                </button>
              ))}
            </div>
          )}
        </Field>

        <div className="mt-3">
          <Textarea
            rows={5}
            value={reflection}
            maxLength={20000}
            onChange={(event) => setReflection(event.target.value)}
            onBlur={() => {
              if (reflection.trim() !== (prep.reflection ?? '').trim()) saveReflectionNotes()
            }}
            placeholder="What went well, what you fumbled, what you'd say differently…"
            aria-label="How it went"
          />
        </div>
        {saveReflection.error && <Banner>{saveReflection.error}</Banner>}
      </Card>

      <InterviewDebriefPanel
        debrief={prep.debrief}
        busy={debrief.busy}
        blockedReason={debriefBlockedReason}
        onGenerate={() =>
          void debrief.run(async () => {
            await api.generateInterviewDebrief(interviewId)
            await load()
            toast.success('Debrief ready.')
          })
        }
      />

      {debrief.error && <Banner>{debrief.error}</Banner>}
    </div>
  )
}
