import { useMemo, useState } from 'react'
import type { Application, ApplicationStatus, MatchResult } from '../api/types'
import { STATUS_ORDER } from '../lib/status'
import { formatDate, formatRelative } from '../lib/format'
import { InlineStatusSelect } from './InlineStatusSelect'
import { MatchScoreCell } from './MatchScoreCell'

type SortKey = 'dateApplied' | 'status' | 'companyName' | 'updatedAtUtc' | 'matchScore'

interface Props {
  applications: Application[]
  matches: Record<string, MatchResult>
  busyId: string | null
  scoringId: string | null
  onStatusChange: (id: string, status: ApplicationStatus) => void
  onScore: (application: Application) => void
  onOpenMatch: (application: Application) => void
  onOpenTools: (application: Application) => void
  onEdit: (application: Application) => void
  onDelete: (application: Application) => void
}

const columns: { key: SortKey; label: string; className?: string }[] = [
  { key: 'companyName', label: 'Company / Role' },
  { key: 'status', label: 'Status' },
  { key: 'matchScore', label: 'Match' },
  { key: 'dateApplied', label: 'Applied' },
  { key: 'updatedAtUtc', label: 'Last activity', className: 'hidden md:table-cell' },
]

export function ApplicationsTable({
  applications,
  matches,
  busyId,
  scoringId,
  onStatusChange,
  onScore,
  onOpenMatch,
  onOpenTools,
  onEdit,
  onDelete,
}: Props) {
  const [sortKey, setSortKey] = useState<SortKey>('dateApplied')
  const [sortAsc, setSortAsc] = useState(false)

  const sorted = useMemo(() => {
    const compare = (a: Application, b: Application): number => {
      switch (sortKey) {
        case 'companyName':
          return a.companyName.localeCompare(b.companyName)
        case 'status':
          return STATUS_ORDER[a.status] - STATUS_ORDER[b.status]
        case 'dateApplied':
          return a.dateApplied.localeCompare(b.dateApplied)
        case 'updatedAtUtc':
          return a.updatedAtUtc.localeCompare(b.updatedAtUtc)
        case 'matchScore':
          // Unscored rows sort below every scored one in either direction.
          return (matches[a.id]?.score ?? -1) - (matches[b.id]?.score ?? -1)
      }
    }
    const list = [...applications].sort(compare)
    return sortAsc ? list : list.reverse()
  }, [applications, matches, sortKey, sortAsc])

  const toggleSort = (key: SortKey) => {
    if (key === sortKey) {
      setSortAsc((v) => !v)
    } else {
      setSortKey(key)
      setSortAsc(key === 'companyName' || key === 'status')
    }
  }

  return (
    <div className="overflow-visible rounded-2xl bg-white shadow-sm ring-1 ring-slate-200">
      <table className="w-full text-left">
        <thead>
          <tr className="border-b-2 border-slate-100">
            {columns.map((column) => {
              const active = sortKey === column.key
              return (
                <th
                  key={column.key}
                  scope="col"
                  className={`px-5 py-4 ${column.className ?? ''}`}
                  aria-sort={active ? (sortAsc ? 'ascending' : 'descending') : 'none'}
                >
                  <button
                    type="button"
                    onClick={() => toggleSort(column.key)}
                    className={`inline-flex items-center gap-1.5 text-sm font-bold uppercase tracking-wide transition ${
                      active ? 'text-brand-700' : 'text-slate-500 hover:text-slate-800'
                    }`}
                  >
                    {column.label}
                    <span className={active ? 'opacity-100' : 'opacity-0'} aria-hidden>
                      {sortAsc ? '↑' : '↓'}
                    </span>
                  </button>
                </th>
              )
            })}
            <th scope="col" className="px-5 py-4">
              <span className="sr-only">Actions</span>
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {sorted.map((application) => (
            <tr key={application.id} className="group transition hover:bg-brand-50/40">
              <td className="px-5 py-4">
                <div className="text-base font-semibold text-slate-900">
                  {application.companyName}
                </div>
                <div className="mt-0.5 text-sm text-slate-500">
                  {application.roleTitle}
                  {application.jobPostingUrl && (
                    <>
                      {' · '}
                      <a
                        href={application.jobPostingUrl}
                        target="_blank"
                        rel="noreferrer"
                        className="font-medium text-brand-600 hover:underline"
                      >
                        posting ↗
                      </a>
                    </>
                  )}
                </div>
              </td>
              <td className="px-5 py-4">
                <InlineStatusSelect
                  value={application.status}
                  disabled={busyId === application.id}
                  onChange={(status) => onStatusChange(application.id, status)}
                />
              </td>
              <td className="px-5 py-4">
                <MatchScoreCell
                  match={matches[application.id]}
                  scoring={scoringId === application.id}
                  hasJobDescription={Boolean(application.jobDescriptionText)}
                  onScore={() => onScore(application)}
                  onOpen={() => onOpenMatch(application)}
                />
              </td>
              <td className="px-5 py-4 whitespace-nowrap text-base text-slate-600">
                {formatDate(application.dateApplied)}
              </td>
              <td className="hidden px-5 py-4 whitespace-nowrap text-base text-slate-500 md:table-cell">
                {formatRelative(application.updatedAtUtc)}
              </td>
              <td className="px-5 py-4 text-right whitespace-nowrap">
                <div className="flex justify-end gap-1 opacity-0 transition group-hover:opacity-100 focus-within:opacity-100">
                  <button
                    type="button"
                    onClick={() => onOpenTools(application)}
                    className="rounded-lg px-3 py-1.5 text-sm font-semibold text-brand-600 hover:bg-brand-50"
                  >
                    ✨ AI tools
                  </button>
                  <button
                    type="button"
                    onClick={() => onEdit(application)}
                    className="rounded-lg px-3 py-1.5 text-sm font-semibold text-slate-600 hover:bg-slate-100"
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    onClick={() => onDelete(application)}
                    className="rounded-lg px-3 py-1.5 text-sm font-semibold text-rose-600 hover:bg-rose-50"
                  >
                    Delete
                  </button>
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
