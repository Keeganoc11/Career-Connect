import { useRef } from 'react'
import { Modal } from './Modal'
import { Button } from './Button'

interface Props {
  title: string
  body: string
  /** The verb, matching the title — "Delete application", "Disconnect Gmail". */
  confirmLabel: string
  /** Shown while the action runs. The old dialog said "Deleting…" for everything. */
  busyLabel?: string
  busy?: boolean
  /** Kept open on failure so this has somewhere to render. */
  error?: string | null
  onConfirm: () => void
  onCancel: () => void
}

/**
 * The one confirmation. Cancel takes initial focus so a stray Return can't
 * confirm a delete, and a failure leaves the dialog open with the reason in it
 * rather than closing as though it worked.
 */
export function ConfirmDialog({
  title,
  body,
  confirmLabel,
  busyLabel,
  busy = false,
  error,
  onConfirm,
  onCancel,
}: Props) {
  const cancelRef = useRef<HTMLButtonElement>(null)

  return (
    <Modal
      title={title}
      size="sm"
      busy={busy}
      error={error}
      initialFocus={cancelRef}
      onClose={onCancel}
      footer={
        <>
          <Button ref={cancelRef} onClick={onCancel} disabled={busy}>
            Cancel
          </Button>
          <Button variant="danger" loading={busy} onClick={onConfirm}>
            {busy && busyLabel ? busyLabel : confirmLabel}
          </Button>
        </>
      }
    >
      <p className="text-sm leading-relaxed text-fg-muted">{body}</p>
    </Modal>
  )
}
