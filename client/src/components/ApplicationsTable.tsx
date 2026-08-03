import { useMemo, useState } from 'react'
import type { Application, ApplicationStatus } from '../api/types'
import { STATUS_ORDER } from '../lib/status'
import { formatDate, formatRelative } from '../lib/format'
import { InlineStatusSelect } from './InlineStatusSelect'

type SortKey = 'dateApplied' | 'status' | 'companyName' | 'updatedAtUtc'

interface Props {
  applications: Application[]
  busyId: string | null
  onStatusChange: (id: string, status: ApplicationStatus) => void
  onEdit: (application: Application) => void
  onDelete: (application: Application) => void
}

const columns: { key: SortKey; label: string; className?: string }[] = [
  { key: 'companyName', label: 'Company / Role' },
  { key: 'status', label: 'Status' },
  { key: 'dateApplied', label: 'Applied' },
  { key: 'updatedAtUtc', label: 'Last activity', className: 'hidden md:table-cell' },
]

export function ApplicationsTable({ applications, busyId, onStatusChange, onEdit, onDelete }: Props) {
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
      }
    }
    const list = [...applications].sort(compare)
    return sortAsc ? list : list.reverse()
  }, [applications, sortKey, sortAsc])

  const toggleSort = (key: SortKey) => {
    if (key === sortKey) {
      setSortAsc((v) => !v)
    } else {
      setSortKey(key)
      setSortAsc(key === 'companyName' || key === 'status')
    }
  }

  return (
    <div className="overflow-visible rounded-xl bg-white shadow-sm ring-1 ring-slate-200">
      <table className="w-full text-left text-sm">
        <thead>
          <tr className="border-b border-slate-200 text-xs uppercase tracking-wide text-slate-500">
            {columns.map((column) => (
              <th key={column.key} className={`px-4 py-3 font-medium ${column.className ?? ''}`}>
                <button
                  type="button"
                  onClick={() => toggleSort(column.key)}
                  className="inline-flex items-center gap-1 hover:text-slate-800"
                >
                  {column.label}
                  {sortKey === column.key && (
                    <span aria-hidden>{sortAsc ? '↑' : '↓'}</span>
                  )}
                </button>
              </th>
            ))}
            <th className="px-4 py-3" />
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-100">
          {sorted.map((application) => (
            <tr key={application.id} className="group hover:bg-slate-50/70">
              <td className="px-4 py-3">
                <div className="font-medium text-slate-900">{application.companyName}</div>
                <div className="text-slate-500">
                  {application.roleTitle}
                  {application.jobPostingUrl && (
                    <>
                      {' · '}
                      <a
                        href={application.jobPostingUrl}
                        target="_blank"
                        rel="noreferrer"
                        className="text-indigo-600 hover:underline"
                      >
                        posting ↗
                      </a>
                    </>
                  )}
                </div>
              </td>
              <td className="px-4 py-3">
                <InlineStatusSelect
                  value={application.status}
                  disabled={busyId === application.id}
                  onChange={(status) => onStatusChange(application.id, status)}
                />
              </td>
              <td className="px-4 py-3 whitespace-nowrap text-slate-600">
                {formatDate(application.dateApplied)}
              </td>
              <td className="hidden px-4 py-3 whitespace-nowrap text-slate-500 md:table-cell">
                {formatRelative(application.updatedAtUtc)}
              </td>
              <td className="px-4 py-3 text-right whitespace-nowrap">
                <div className="flex justify-end gap-1 opacity-0 transition group-hover:opacity-100 focus-within:opacity-100">
                  <button
                    type="button"
                    onClick={() => onEdit(application)}
                    className="rounded-md px-2 py-1 text-xs font-medium text-slate-600 hover:bg-slate-100"
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    onClick={() => onDelete(application)}
                    className="rounded-md px-2 py-1 text-xs font-medium text-rose-600 hover:bg-rose-50"
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
