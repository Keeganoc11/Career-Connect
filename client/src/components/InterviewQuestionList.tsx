import { useEffect, useRef, useState, type FormEvent } from 'react'
import { Check, Plus, Trash2 } from 'lucide-react'
import { api } from '../api/client'
import {
  ANSWER_QUALITIES,
  QUESTION_KINDS,
  type AnswerQuality,
  type InterviewQuestionKind,
  type InterviewQuestionSide,
  type QuestionInput,
  type TrackedQuestion,
} from '../api/types'
import { QUALITY_LABELS, QUESTION_KIND_LABELS } from '../lib/interviewQuestions'
import { useAsyncAction } from '../lib/useAsyncAction'
import { Badge, Banner, Button, Card, IconButton, Input, Select, Textarea, toast } from './ui'

interface Props {
  interviewId: string
  side: InterviewQuestionSide
  questions: TrackedQuestion[]
  /** A whole refetch, so the parent stays the one source of the page's data. */
  onChanged: () => Promise<void>
  /** Only offered for questions they ask; suggesting what to ask them is its own button. */
  header: React.ReactNode
}

/**
 * One side of a round's questions.
 *
 * Both sides are the same list with different meanings: a question they asked
 * carries your answer and how it went, one you plan to ask carries their answer
 * and whether you got to it. Same component, because the editing behaviour —
 * save on blur, delete with no dialog — should feel identical.
 */
export function InterviewQuestionList({ interviewId, side, questions, onChanged, header }: Props) {
  const [text, setText] = useState('')
  const [kind, setKind] = useState<InterviewQuestionKind>(
    side === 'TheyAsked' ? 'Behavioral' : 'Role',
  )
  const add = useAsyncAction()

  const mine = questions
    .filter((question) => question.side === side)
    .sort((a, b) => a.position - b.position)

  const submit = (event: FormEvent) => {
    event.preventDefault()
    if (!text.trim()) return

    void add.run(async () => {
      await api.addInterviewQuestion(interviewId, side, { text: text.trim(), kind })
      setText('')
      await onChanged()
    })
  }

  return (
    <Card>
      {header}

      {mine.length > 0 && (
        <ul className="mt-4 space-y-3">
          {mine.map((question) => (
            <QuestionRow key={question.id} question={question} onChanged={onChanged} />
          ))}
        </ul>
      )}

      <form onSubmit={submit} className="mt-4 flex flex-wrap items-center gap-2">
        <div className="min-w-48 flex-1">
          <Input
            type="text"
            value={text}
            maxLength={1000}
            onChange={(event) => setText(event.target.value)}
            placeholder={
              side === 'TheyAsked'
                ? 'Add a question they asked…'
                : 'Add a question you want to ask…'
            }
            aria-label={side === 'TheyAsked' ? 'Question they asked' : 'Question to ask them'}
          />
        </div>
        <div className="w-40">
          <Select
            value={kind}
            aria-label="Question type"
            onChange={(event) => setKind(event.target.value as InterviewQuestionKind)}
          >
            {QUESTION_KINDS.map((value) => (
              <option key={value} value={value}>
                {QUESTION_KIND_LABELS[value]}
              </option>
            ))}
          </Select>
        </div>
        <Button
          type="submit"
          icon={<Plus className="size-4" aria-hidden />}
          loading={add.busy}
          disabled={!text.trim()}
        >
          Add
        </Button>
      </form>

      {add.error && <Banner>{add.error}</Banner>}
    </Card>
  )
}

function QuestionRow({
  question,
  onChanged,
}: {
  question: TrackedQuestion
  onChanged: () => Promise<void>
}) {
  const [answer, setAnswer] = useState(question.answer ?? '')
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  const save = useAsyncAction()
  const remove = useAsyncAction()

  const theyAsked = question.side === 'TheyAsked'

  /**
   * The row's current state, not the server's.
   *
   * Clicking a rating while the answer box has focus fires both a blur-save
   * and the rating in the same tick. The update is a whole-question PUT, so
   * two of them built from the props each clobbered the other's field — in
   * practice the rating vanished. Both now send the same merged state, and a
   * ref rather than state because the second call reads it before React has
   * re-rendered.
   */
  const latest = useRef<QuestionInput>({
    text: question.text,
    kind: question.kind,
    answer: question.answer,
    quality: question.quality,
    asked: question.asked,
  })

  // A refetch is the server agreeing with what was sent — but if it disagrees
  // (another tab, a failed save), the row follows the server.
  useEffect(() => {
    if (save.busy) return
    latest.current = {
      text: question.text,
      kind: question.kind,
      answer: question.answer,
      quality: question.quality,
      asked: question.asked,
    }
  }, [question, save.busy])

  const patch = (changes: Partial<QuestionInput>) => {
    latest.current = { ...latest.current, ...changes }
    const payload = latest.current
    return save.run(async () => {
      await api.updateInterviewQuestion(question.id, payload)
      await onChanged()
    })
  }

  // Saved when the field is left, not on every keystroke: it's a paragraph
  // being typed, and a request per character would fight the cursor.
  const saveAnswer = () => {
    if (answer.trim() === (latest.current.answer ?? '').trim()) return
    void patch({ answer: answer.trim() || null })
  }

  return (
    <li className="rounded-surface border border-line p-3">
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-sm text-fg">{question.text}</p>
          <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
            <Badge>{QUESTION_KIND_LABELS[question.kind]}</Badge>
            {question.suggested && <Badge emphasis="accent">Suggested</Badge>}
          </div>
        </div>
        <IconButton
          label={`Remove “${question.text}”`}
          icon={<Trash2 className="size-4" aria-hidden />}
          onClick={() => setConfirmingDelete(true)}
        />
      </div>

      <div className="mt-3">
        <Textarea
          rows={2}
          value={answer}
          maxLength={8000}
          onChange={(event) => setAnswer(event.target.value)}
          onBlur={saveAnswer}
          placeholder={theyAsked ? 'What you said…' : 'What they said…'}
          aria-label={theyAsked ? 'Your answer' : 'Their answer'}
        />
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-2">
        {theyAsked ? (
          ANSWER_QUALITIES.map((value) => (
            <QualityButton
              key={value}
              value={value}
              selected={question.quality === value}
              busy={save.busy}
              // Pressing the selected one again clears it — otherwise a
              // misclick is permanent.
              onSelect={() =>
                void patch({ quality: latest.current.quality === value ? null : value })
              }
            />
          ))
        ) : (
          <Button
            size="sm"
            variant={question.asked ? 'primary' : 'secondary'}
            icon={question.asked ? <Check className="size-4" aria-hidden /> : undefined}
            loading={save.busy}
            onClick={() => void patch({ asked: !latest.current.asked })}
          >
            {question.asked ? 'Asked' : 'Mark asked'}
          </Button>
        )}
      </div>

      {save.error && <Banner>{save.error}</Banner>}
      {remove.error && <Banner>{remove.error}</Banner>}

      {confirmingDelete && (
        <div className="mt-3 flex flex-wrap items-center justify-between gap-2 rounded-control bg-surface-muted p-2">
          <span className="text-sm text-fg-muted">Remove this question?</span>
          <div className="flex gap-2">
            <Button size="sm" onClick={() => setConfirmingDelete(false)}>
              Keep
            </Button>
            <Button
              size="sm"
              tone="danger"
              loading={remove.busy}
              onClick={() =>
                void remove.run(async () => {
                  await api.deleteInterviewQuestion(question.id)
                  toast.success('Question removed.')
                  await onChanged()
                })
              }
            >
              Remove
            </Button>
          </div>
        </div>
      )}
    </li>
  )
}

function QualityButton({
  value,
  selected,
  busy,
  onSelect,
}: {
  value: AnswerQuality
  selected: boolean
  busy: boolean
  onSelect: () => void
}) {
  return (
    <button
      type="button"
      disabled={busy}
      onClick={onSelect}
      aria-pressed={selected}
      className={`rounded-control border px-2.5 py-1 text-xs transition-colors disabled:opacity-60 ${
        selected
          ? 'border-accent bg-accent-soft font-medium text-accent'
          : 'border-line text-fg-muted hover:text-fg'
      }`}
    >
      {QUALITY_LABELS[value]}
    </button>
  )
}
