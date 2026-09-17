import { useRef, useState } from 'react'
import {
  ArrowRight,
  CalendarDays,
  ExternalLink,
  FileText,
  MessageSquareText,
  MoreHorizontal,
  Pencil,
  Trash2,
} from 'lucide-react'
import type { Application } from '../api/types'
import { usePlan } from '../lib/planContext'
import { IconButton, Menu, type MenuItem } from './ui'

interface Props {
  application: Application
  onOpen: () => void
  onEdit: () => void
  onInterviews: () => void
  onCoverLetter: () => void
  onInterviewPrep: () => void
  onDelete: () => void
}

/**
 * Always visible, never hover-only.
 *
 * The row actions this replaces lived behind `opacity-0 group-hover:opacity-100`,
 * so on a touch screen they were unreachable and with a keyboard you couldn't
 * see what you'd focused.
 */
export function ApplicationActionsMenu({
  application,
  onOpen,
  onEdit,
  onInterviews,
  onCoverLetter,
  onInterviewPrep,
  onDelete,
}: Props) {
  const { isPro } = usePlan()
  const buttonRef = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)

  const items: MenuItem[] = [
    {
      key: 'open',
      label: 'Open',
      icon: <ArrowRight className="size-4" aria-hidden />,
      onSelect: onOpen,
    },
    {
      key: 'edit',
      label: 'Edit details',
      icon: <Pencil className="size-4" aria-hidden />,
      onSelect: onEdit,
    },
    {
      key: 'interviews',
      label: 'Interviews',
      icon: <CalendarDays className="size-4" aria-hidden />,
      onSelect: onInterviews,
    },
  ]

  // Both are written by a model. A Free menu shows what it can actually do
  // rather than items that open a dialog only to refuse.
  if (isPro) {
    items.push(
      {
        key: 'cover-letter',
        label: 'Cover letter',
        icon: <FileText className="size-4" aria-hidden />,
        onSelect: onCoverLetter,
      },
      {
        key: 'interview-prep',
        label: 'Interview prep',
        icon: <MessageSquareText className="size-4" aria-hidden />,
        onSelect: onInterviewPrep,
      },
    )
  }

  if (application.jobPostingUrl) {
    items.push({
      key: 'posting',
      label: 'Open job posting',
      icon: <ExternalLink className="size-4" aria-hidden />,
      onSelect: () => window.open(application.jobPostingUrl!, '_blank', 'noopener,noreferrer'),
    })
  }

  // Menu draws a separator before the first destructive item, so Delete is
  // never adjacent to a routine action.
  items.push({
    key: 'delete',
    label: 'Delete application',
    icon: <Trash2 className="size-4" aria-hidden />,
    destructive: true,
    onSelect: onDelete,
  })

  return (
    <>
      <IconButton
        ref={buttonRef}
        label={`Actions for ${application.companyName}`}
        icon={<MoreHorizontal className="size-4" aria-hidden />}
        onClick={() => setOpen((v) => !v)}
      />
      {open && (
        <Menu
          anchorRef={buttonRef}
          label={`Actions for ${application.companyName}`}
          onClose={() => setOpen(false)}
          items={items}
        />
      )}
    </>
  )
}
