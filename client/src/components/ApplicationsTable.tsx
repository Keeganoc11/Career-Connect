import { ArrowDown, ArrowUp, ExternalLink } from 'lucide-react'
import type { Application, ApplicationStatus, MatchResult, PrepRun } from '../api/types'
import { formatDate, formatRelative, formatUntil } from '../lib/format'
import { nextInterview } from '../lib/interviews'
import type { SortKey } from '../lib/sortApplications'
import { ApplicationActionsMenu } from './ApplicationActionsMenu'
import { MatchScoreCell } from './MatchScoreCell'
import { PrepStatusCell } from './PrepStatusCell'
import { StatusMenu } from './StatusMenu'
import { Badge, IconButton } from './ui'

export interface ApplicationsViewProps {
  /** Already filtered and sorted by the page. */
  applications: Application[]
  matches: Record<string, MatchResult>
  prepRuns: Record<string, PrepRun>
  busyId: string | null
  scoringId: string | null
  onStatusChange: (id: string, status: ApplicationStatus) => void
  onScore: (application: Application) => void
  onOpenMatch: (application: Application) => void
  onOpenPrep: (application: Application) => void
  onOpenInterviews: (application: Application) => void
  onOpenCoverLetter: (application: Application) => void
  onOpenInterviewPrep: (application: Application) => void
  onEdit: (application: Application) => void
  onDelete: (application: Application) => void
}

interface Props extends ApplicationsViewProps {
  sortKey: SortKey
  sortAsc: boolean
  onSort: (key: SortKey) => void
}

const COLUMNS: { key: SortKey; label: string; className?: string }[] = [
  { key: 'companyName', label: 'Application' },
  { key: 'status', label: 'Status' },
  { key: 'matchScore', label: 'Match' },
  { key: 'dateApplied', label: 'Date' },
  { key: 'updatedAtUtc', label: 'Last activity', className: 'hidden lg:table-cell' },
]

/**
 * Desktop only — below md the page renders ApplicationsList instead, so the
 * 800px minimum width that forced horizontal scrolling on a phone is gone.
 */
export function ApplicationsTable({
  applications,
  matches,
  prepRuns,
  busyId,
  scoringId,
  sortKey,
  sortAsc,
  onSort,
  onStatusChange,
  onScore,
  onOpenMatch,
  onOpenPrep,
  onOpenInterviews,
  onOpenCoverLetter,
  onOpenInterviewPrep,
  onEdit,
  onDelete,
}: Props) {
  return (
    <div className="overflow-hidden rounded-surface border border-line bg-surface">
      <table className="w-full text-left">
        <thead>
          <tr className="border-b border-line">
            {COLUMNS.map((column) => {
              const active = sortKey === column.key
              const Arrow = sortAsc ? ArrowUp : ArrowDown
              return (
                <th
                  key={column.key}
                  scope="col"
                  className={`px-4 py-2.5 ${column.className ?? ''}`}
                  aria-sort={active ? (sortAsc ? 'ascending' : 'descending') : 'none'}
                >
                  <button
                    type="button"
                    onClick={() => onSort(column.key)}
                    className={`inline-flex items-center gap-1 text-xs font-medium transition-colors ${
                      active ? 'text-fg' : 'text-fg-muted hover:text-fg'
                    }`}
                  >
                    {column.label}
                    <Arrow className={`size-3 ${active ? 'opacity-100' : 'opacity-0'}`} aria-hidden />
                  </button>
                </th>
              )
            })}
            <th scope="col" className="px-4 py-2.5">
              <span className="text-xs font-medium text-fg-muted">Prep</span>
            </th>
            <th scope="col" className="px-4 py-2.5">
              <span className="sr-only">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line">
          {applications.map((application) => {
            const interview = nextInterview(application)
            return (
              <tr key={application.id} className="transition-colors hover:bg-surface-muted">
                <td className="px-4 py-3">
                  <div className="flex items-center gap-1.5">
                    <button
                      type="button"
                      onClick={() => onEdit(application)}
                      className="rounded-sm text-sm font-medium text-fg hover:text-accent"
                    >
                      {application.companyName}
                    </button>
                    {application.jobPostingUrl && (
                      <IconButton
                        label={`Open job posting for ${application.companyName}`}
                        icon={<ExternalLink className="size-3.5" aria-hidden />}
                        onClick={() =>
                          window.open(application.jobPostingUrl!, '_blank', 'noopener,noreferrer')
                        }
                      />
                    )}
                  </div>
                  <div className="text-sm text-fg-muted">{application.roleTitle}</div>
                  {interview && (
                    <button
                      type="button"
                      onClick={() => onOpenInterviews(application)}
                      className="mt-1 rounded-full"
                    >
                      <Badge emphasis="interview">{formatUntil(interview.scheduledAtUtc)}</Badge>
                    </button>
                  )}
                </td>
                <td className="px-4 py-3">
                  <StatusMenu
                    value={application.status}
                    disabled={busyId === application.id}
                    onChange={(status) => onStatusChange(application.id, status)}
                  />
                </td>
                <td className="px-4 py-3">
                  <MatchScoreCell
                    match={matches[application.id]}
                    scoring={scoringId === application.id}
                    hasJobDescription={Boolean(application.jobDescriptionText)}
                    onScore={() => onScore(application)}
                    onOpen={() => onOpenMatch(application)}
                  />
                </td>
                <td className="px-4 py-3 text-sm whitespace-nowrap text-fg-muted tabular-nums">
                  {/* A Preparing row hasn't been applied to yet, so the date is
                      a target rather than a record of what happened. */}
                  {application.status === 'Preparing' && 'Target · '}
                  {formatDate(application.dateApplied)}
                </td>
                <td className="hidden px-4 py-3 text-sm whitespace-nowrap text-fg-muted lg:table-cell">
                  {formatRelative(application.updatedAtUtc)}
                </td>
                <td className="px-4 py-3">
                  <PrepStatusCell
                    run={prepRuns[application.id]}
                    hasJobDescription={Boolean(application.jobDescriptionText)}
                    onOpen={() => onOpenPrep(application)}
                  />
                </td>
                <td className="px-4 py-3 text-right">
                  <ApplicationActionsMenu
                    application={application}
                    onEdit={() => onEdit(application)}
                    onInterviews={() => onOpenInterviews(application)}
                    onPrep={() => onOpenPrep(application)}
                    onMatch={() => onOpenMatch(application)}
                    onCoverLetter={() => onOpenCoverLetter(application)}
                    onInterviewPrep={() => onOpenInterviewPrep(application)}
                    onDelete={() => onDelete(application)}
                  />
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
