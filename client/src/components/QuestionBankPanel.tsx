import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { QuestionBank, QuestionBankEntry } from '../api/types'
import { formatRelative } from '../lib/format'
import { QUESTION_KIND_LABELS } from '../lib/interviewQuestions'
import { Badge, Card } from './ui'

interface Props {
  /** Bumped when anything changed, so a question logged elsewhere shows up here. */
  dataVersion: number
  onOpenInterview: (interviewId: string) => void
}

/**
 * Every question you've actually been asked, across every interview.
 *
 * The point is repetition: the same behavioral question comes back at four
 * companies, and the third time you fumble it you should be able to see that
 * you fumbled it the first two. So the list leads with what to rehearse, and
 * the count is per question rather than per interview.
 */
export function QuestionBankPanel({ dataVersion, onOpenInterview }: Props) {
  const [bank, setBank] = useState<QuestionBank | null>(null)
  const [showAll, setShowAll] = useState(false)

  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const loaded = await api.getQuestionBank()
        if (!cancelled) setBank(loaded)
      } catch {
        // A quiet panel: the rest of the agenda already reports an outage, and
        // this one has nothing to say until there are interviews behind it.
      }
    })()
    return () => {
      cancelled = true
    }
  }, [dataVersion])

  // Nothing logged yet is the normal state early on, and an empty card
  // explaining an empty card is noise.
  if (!bank || bank.questions.length === 0) return null

  const shown = showAll ? bank.questions : bank.practice.length > 0 ? bank.practice : bank.questions

  return (
    <section>
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <h2 className="text-base font-semibold text-fg">Questions you’ve been asked</h2>
        <button
          type="button"
          onClick={() => setShowAll((v) => !v)}
          className="rounded-sm text-sm font-medium text-accent hover:text-accent-hover"
        >
          {showAll
            ? 'Show what to rehearse'
            : `Show all ${bank.questions.length}`}
        </button>
      </div>

      {!showAll && bank.practice.length > 0 && (
        <p className="mb-2 text-sm text-fg-muted">
          The ones that went badly, or that keep coming back with no answer written down.
        </p>
      )}

      <Card padded={false}>
        <ul className="divide-y divide-line">
          {shown.map((entry) => (
            <BankRow key={entry.text} entry={entry} onOpenInterview={onOpenInterview} />
          ))}
        </ul>
      </Card>
    </section>
  )
}

function BankRow({
  entry,
  onOpenInterview,
}: {
  entry: QuestionBankEntry
  onOpenInterview: (interviewId: string) => void
}) {
  return (
    <li className="px-4 py-3">
      <p className="text-sm text-fg">{entry.text}</p>

      <div className="mt-1.5 flex flex-wrap items-center gap-1.5">
        <Badge>{QUESTION_KIND_LABELS[entry.kind]}</Badge>
        {entry.timesAsked > 1 && <Badge>Asked {entry.timesAsked} times</Badge>}
        {entry.weakAnswers > 0 && (
          <Badge emphasis="accent">
            {entry.weakAnswers === entry.timesAsked
              ? 'Fumbled every time'
              : `Fumbled ${entry.weakAnswers} of ${entry.timesAsked}`}
          </Badge>
        )}
      </div>

      <p className="mt-1.5 text-xs text-fg-muted">
        {entry.companies.join(' · ')} · last asked {formatRelative(entry.lastAskedAtUtc)}
      </p>

      {entry.lastAnswer ? (
        <p className="mt-2 text-sm text-fg-muted">{entry.lastAnswer}</p>
      ) : (
        <p className="mt-2 text-sm text-fg-subtle">No answer written down.</p>
      )}

      <button
        type="button"
        onClick={() => onOpenInterview(entry.lastInterviewId)}
        className="mt-2 rounded-sm text-sm font-medium text-accent hover:text-accent-hover"
      >
        Open that interview
      </button>
    </li>
  )
}
