import { useCallback, useEffect, useState } from 'react'
import { ArrowRight, Undo2 } from 'lucide-react'
import { api } from '../api/client'
import type { ActivityItem } from '../api/types'
import { errorMessage } from '../lib/errors'
import { formatDateTime, formatRelative } from '../lib/format'
import { KIND_LABELS } from '../lib/interviews'
import { Button, Card, StatusBadge, toast } from './ui'

interface Props {
  dataVersion: number
  /** An undo changed an application, so every page should refetch. */
  onDataChanged: () => void
  onOpenJob?: (applicationId: string) => void
  /** Rendered above the list; the whole section disappears when there's nothing to show. */
  title?: string
}

/**
 * Changes the app made without asking — a rejection read off an email, an
 * interview scheduled from an invite, a job marked ghosted after a month of
 * silence — each with an Undo. Automation is only acceptable because this
 * exists: nothing changes quietly, and nothing changes irreversibly.
 */
export function ActivityFeed({ dataVersion, onDataChanged, onOpenJob, title = 'Done for you' }: Props) {
  const [items, setItems] = useState<ActivityItem[]>([])
  const [undoingId, setUndoingId] = useState<string | null>(null)

  const refresh = useCallback(async () => {
    try {
      setItems(await api.listActivity())
    } catch {
      // The feed is a side panel: a failed load leaves it hidden rather than
      // putting an error on a page that's about something else.
    }
  }, [])

  useEffect(() => {
    void refresh()
  }, [refresh, dataVersion])

  const undo = async (item: ActivityItem) => {
    setUndoingId(item.id)
    try {
      const updated = await api.undoActivity(item.id)
      setItems((current) => current.map((i) => (i.id === item.id ? { ...i, ...updated } : i)))
      onDataChanged()
      toast.success(`${item.companyName} is back to ${statusWord(item.fromStatus)}.`)
    } catch (e) {
      toast.error(errorMessage(e) ?? 'Could not undo that.')
      void refresh()
    } finally {
      setUndoingId(null)
    }
  }

  if (items.length === 0) return null

  return (
    <section>
      <h2 className="mb-0.5 text-base font-semibold text-fg">{title}</h2>
      <p className="mb-2 text-sm text-fg-muted">Changes made automatically in the last two weeks. Undo any that are wrong.</p>
      <Card padded={false}>
        <ul className="divide-y divide-line">
          {items.map((item) => {
            const undone = item.undoneAtUtc !== null
            return (
              <li key={item.id} className={`flex flex-wrap items-start gap-3 px-4 py-3 ${undone ? 'opacity-60' : ''}`}>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                    {onOpenJob ? (
                      <button
                        type="button"
                        onClick={() => onOpenJob(item.applicationId)}
                        className="rounded-sm text-sm font-medium text-fg hover:text-accent"
                      >
                        {item.companyName}
                      </button>
                    ) : (
                      <span className="text-sm font-medium text-fg">{item.companyName}</span>
                    )}
                    <span className="flex items-center gap-1.5">
                      <StatusBadge status={item.fromStatus} />
                      <ArrowRight className="size-3.5 text-fg-subtle" aria-hidden />
                      <StatusBadge status={item.toStatus} />
                    </span>
                  </div>
                  <p className="mt-1 text-sm text-fg-muted">{describe(item)}</p>
                  {item.interviewAtUtc && item.interviewKind && !undone && (
                    <p className="mt-0.5 text-sm text-fg">
                      Scheduled: {KIND_LABELS[item.interviewKind]} · {formatDateTime(item.interviewAtUtc)}
                    </p>
                  )}
                  <p className="mt-0.5 text-xs text-fg-subtle">
                    {undone ? `Undone ${formatRelative(item.undoneAtUtc!)}` : formatRelative(item.createdAtUtc)}
                  </p>
                </div>
                {item.canUndo && (
                  <Button
                    size="sm"
                    icon={<Undo2 className="size-4" aria-hidden />}
                    loading={undoingId === item.id}
                    onClick={() => void undo(item)}
                  >
                    Undo
                  </Button>
                )}
              </li>
            )
          })}
        </ul>
      </Card>
    </section>
  )
}

function describe(item: ActivityItem): string {
  if (item.trigger === 'Inactivity') {
    return item.reasoning ?? 'No reply in a long time.'
  }
  const subject = item.emailSubject ? `“${item.emailSubject}”` : 'an email'
  return item.reasoning ? `${item.reasoning} From ${subject}.` : `From ${subject}.`
}

function statusWord(status: ActivityItem['fromStatus']): string {
  return status === 'PhoneScreen' ? 'phone screen' : status.toLowerCase()
}
