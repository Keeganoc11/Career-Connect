import { useState } from 'react'
import { Sparkles } from 'lucide-react'
import { api } from '../api/client'
import type { CopilotAction, CopilotInsights } from '../api/types'
import { useAsyncAction } from '../lib/useAsyncAction'
import { Badge, Banner, Button, Card } from './ui'

interface Props {
  onOpenApplication: (applicationId: string) => void
}

/**
 * Moved off the applications table, where it sat above the list as "AI
 * insights" and pushed the actual applications down the page. It belongs on
 * the agenda: it answers "what should I do next", which is what this page is.
 *
 * It fetches for itself rather than being handed state, so the agenda doesn't
 * carry a second page's concerns.
 */
export function PipelineReview({ onOpenApplication }: Props) {
  const [insights, setInsights] = useState<CopilotInsights | null>(null)
  const review = useAsyncAction()

  const run = () =>
    void review.run(async () => {
      setInsights(await api.getCopilotInsights())
    })

  return (
    <section>
      <div className="mb-2 flex flex-wrap items-center justify-between gap-3">
        <h2 className="text-base font-semibold text-fg">Pipeline review</h2>
        <Button
          icon={<Sparkles className="size-4" aria-hidden />}
          loading={review.busy}
          onClick={run}
        >
          {insights ? 'Review again' : 'Review pipeline'}
        </Button>
      </div>

      {!insights && !review.busy && !review.error && (
        <p className="text-sm text-fg-muted">
          Reads your whole pipeline at once and says what's worth doing next.
        </p>
      )}

      {review.error && <Banner>{review.error}</Banner>}

      {insights && (
        <Card>
          <p className="text-sm leading-relaxed text-fg">{insights.overallSummary}</p>

          {insights.actions.length > 0 && (
            <ul className="mt-4 divide-y divide-line border-t border-line">
              {insights.actions.map((action, index) => (
                <ActionRow key={index} action={action} onOpen={onOpenApplication} />
              ))}
            </ul>
          )}
        </Card>
      )}
    </section>
  )
}

function ActionRow({
  action,
  onOpen,
}: {
  action: CopilotAction
  onOpen: (applicationId: string) => void
}) {
  return (
    <li className="flex items-start gap-3 py-3">
      {/* Priority is ranking, not feedback, so it stays a quiet chip — the
          accent marks only the ones worth doing first. */}
      <Badge emphasis={action.priority === 'high' ? 'accent' : 'neutral'}>
        {action.priority === 'high' ? 'High' : action.priority === 'medium' ? 'Medium' : 'Low'}
      </Badge>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium text-fg">{action.title}</p>
        <p className="mt-0.5 text-sm text-fg-muted">{action.detail}</p>
      </div>
      {action.applicationId && (
        <Button size="sm" onClick={() => onOpen(action.applicationId!)}>
          Open
        </Button>
      )}
    </li>
  )
}
