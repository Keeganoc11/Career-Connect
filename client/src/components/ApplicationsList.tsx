import type { Application } from '../api/types'
import { formatDate, formatUntil } from '../lib/format'
import { nextInterview } from '../lib/interviews'
import { usePlan } from '../lib/planContext'
import { ApplicationActionsMenu } from './ApplicationActionsMenu'
import type { ApplicationsViewProps } from './ApplicationsTable'
import { MatchScoreCell } from './MatchScoreCell'
import { PrepStatusCell } from './PrepStatusCell'
import { StatusMenu } from './StatusMenu'
import { Badge } from './ui'

/**
 * Below md, where the table's columns can't fit. Same cells and the same sort
 * as the desktop table — it's the same data in a shape that fits, not a
 * reduced version of it.
 */
export function ApplicationsList({
  applications,
  matches,
  prepRuns,
  busyId,
  onStatusChange,
  onOpenJob,
  onOpenInterviews,
  onOpenCoverLetter,
  onOpenInterviewPrep,
  onEdit,
  onDelete,
}: ApplicationsViewProps) {
  const { isPro } = usePlan()

  return (
    <ul className="divide-y divide-line overflow-hidden rounded-surface border border-line bg-surface">
      {applications.map((application: Application) => {
        const interview = nextInterview(application)
        return (
          <li key={application.id} className="p-4">
            <div className="flex items-start justify-between gap-2">
              <button
                type="button"
                onClick={() => onOpenJob(application)}
                className="min-w-0 flex-1 rounded-sm text-left"
              >
                <span className="block truncate text-sm font-medium text-fg">
                  {application.companyName}
                </span>
                <span className="block truncate text-sm text-fg-muted">
                  {application.roleTitle}
                </span>
              </button>
              <ApplicationActionsMenu
                application={application}
                onOpen={() => onOpenJob(application)}
                onEdit={() => onEdit(application)}
                onInterviews={() => onOpenInterviews(application)}
                onCoverLetter={() => onOpenCoverLetter(application)}
                onInterviewPrep={() => onOpenInterviewPrep(application)}
                onDelete={() => onDelete(application)}
              />
            </div>

            <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-2">
              <StatusMenu
                value={application.status}
                disabled={busyId === application.id}
                onChange={(status) => onStatusChange(application.id, status)}
              />
              {isPro && (
                <>
                  <MatchScoreCell
                    match={matches[application.id]}
                    onOpen={() => onOpenJob(application)}
                  />
                  <PrepStatusCell
                    run={prepRuns[application.id]}
                    hasJobDescription={Boolean(application.jobDescriptionText)}
                    onOpen={() => onOpenJob(application)}
                  />
                </>
              )}
            </div>

            <div className="mt-2 flex flex-wrap items-center gap-2 text-xs text-fg-muted">
              <span className="tabular-nums">
                {application.status === 'Preparing' && 'Target · '}
                {formatDate(application.dateApplied)}
              </span>
              {interview && (
                <button type="button" onClick={() => onOpenInterviews(application)}>
                  <Badge emphasis="interview">{formatUntil(interview.scheduledAtUtc)}</Badge>
                </button>
              )}
            </div>
          </li>
        )
      })}
    </ul>
  )
}
