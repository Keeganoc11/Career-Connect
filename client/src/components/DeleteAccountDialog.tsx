import { useState } from 'react'
import { api, auth } from '../api/client'
import { errorMessage } from '../lib/errors'
import { useAsyncAction } from '../lib/useAsyncAction'
import { Banner, Button, Field, Input, Modal } from './ui'

interface Props {
  onClose: () => void
  /** Called once the account is gone, so the app can drop the session and return to the front page. */
  onDeleted: () => void
}

/**
 * The one irreversible thing in the app, so it asks for the account's email to
 * be typed rather than settling for a Yes button. It also says plainly what
 * goes, and offers the export first — deleting shouldn't be the only way to
 * leave with your data.
 */
export function DeleteAccountDialog({ onClose, onDeleted }: Props) {
  const [typed, setTyped] = useState('')
  const [exportError, setExportError] = useState<string | null>(null)
  const remove = useAsyncAction()
  const exporting = useAsyncAction()

  const email = auth.email ?? ''
  const matches = typed.trim().toLowerCase() === email.toLowerCase()

  const submit = () =>
    void remove.run(async () => {
      await api.deleteAccount(typed.trim())
      onDeleted()
    })

  return (
    <Modal
      title="Delete your account"
      size="sm"
      busy={remove.busy}
      error={remove.error}
      onClose={onClose}
      footer={
        <>
          <Button onClick={onClose}>Keep my account</Button>
          <Button variant="danger" loading={remove.busy} disabled={!matches} onClick={submit}>
            Delete account
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-fg-muted">
          This removes your applications, resumes, interviews and notes, and hands back Gmail access
          at Google. It happens immediately and can’t be undone.
        </p>

        <div className="rounded-surface border border-line bg-surface-muted p-3">
          <p className="text-sm text-fg">Take your data with you first?</p>
          <p className="mt-0.5 text-sm text-fg-muted">One file with everything in this account.</p>
          <div className="mt-2">
            <Button
              size="sm"
              loading={exporting.busy}
              onClick={() =>
                void exporting.run(async () => {
                  setExportError(null)
                  try {
                    await api.exportAccount()
                  } catch (e) {
                    setExportError(errorMessage(e))
                  }
                })
              }
            >
              Download my data
            </Button>
          </div>
          {exportError && <Banner>{exportError}</Banner>}
        </div>

        <Field label={`Type ${email} to confirm`}>
          {(props) => (
            <Input
              {...props}
              type="email"
              value={typed}
              autoComplete="off"
              onChange={(event) => setTyped(event.target.value)}
              placeholder={email}
            />
          )}
        </Field>
      </div>
    </Modal>
  )
}
