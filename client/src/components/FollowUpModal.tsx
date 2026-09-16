import { useEffect, useRef, useState } from 'react'
import { ExternalLink, RefreshCw } from 'lucide-react'
import { api } from '../api/client'
import { useAsyncAction } from '../lib/useAsyncAction'
import { Button, CopyButton, Field, Input, LoadingState, Modal, Textarea, toast } from './ui'

interface Props {
  applicationId: string
  companyName: string
  roleTitle: string
  onSent: () => void
  onClose: () => void
}

/**
 * A follow-up to send yourself. The app's Gmail access is read-only on
 * purpose, so this hands over a draft — copy it, or open it in Gmail's compose
 * window — and "I sent it" is what restarts the silence clock.
 */
export function FollowUpModal({ applicationId, companyName, roleTitle, onSent, onClose }: Props) {
  const [subject, setSubject] = useState('')
  const [body, setBody] = useState('')
  const draft = useAsyncAction()
  const sent = useAsyncAction()
  const started = useRef(false)

  const write = () =>
    void draft.run(async () => {
      const result = await api.draftFollowUp(applicationId)
      setSubject(result.subject)
      setBody(result.body)
    })

  useEffect(() => {
    // Once per open — StrictMode's double mount would otherwise draft twice.
    if (started.current) return
    started.current = true
    write()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const gmailUrl =
    'https://mail.google.com/mail/?view=cm&fs=1' +
    `&su=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`

  const markSent = () =>
    void sent.run(async () => {
      await api.markFollowedUp(applicationId)
      toast.success(`Follow-up to ${companyName} noted.`)
      onSent()
      onClose()
    })

  const ready = !draft.busy && body.length > 0

  return (
    <Modal
      title="Follow up"
      description={`${companyName} · ${roleTitle}`}
      error={draft.error ?? sent.error}
      busy={sent.busy}
      onClose={onClose}
      footerStart={
        <Button
          variant="ghost"
          icon={<RefreshCw className="size-4" aria-hidden />}
          loading={draft.busy}
          onClick={write}
        >
          Write another
        </Button>
      }
      footer={
        <>
          <Button onClick={onClose} disabled={sent.busy}>
            Cancel
          </Button>
          <Button variant="primary" loading={sent.busy} disabled={!ready} onClick={markSent}>
            I sent it
          </Button>
        </>
      }
    >
      {draft.busy && !body ? (
        <LoadingState label="Writing your follow-up…" />
      ) : (
        <div className="space-y-4">
          <Field label="Subject">
            {(props) => (
              <div className="flex gap-2">
                <Input {...props} value={subject} onChange={(e) => setSubject(e.target.value)} />
                <CopyButton text={subject} />
              </div>
            )}
          </Field>
          <Field label="Email" hint="Edit freely. Nothing is sent from here — send it from your own inbox.">
            {(props) => (
              <Textarea {...props} rows={9} value={body} onChange={(e) => setBody(e.target.value)} />
            )}
          </Field>
          <div className="flex flex-wrap gap-2">
            <CopyButton text={body} label="Copy email" size="md" />
            <a
              href={gmailUrl}
              target="_blank"
              rel="noreferrer noopener"
              className="inline-flex h-9 items-center gap-2 rounded-control border border-line bg-surface px-4 text-sm font-medium text-fg hover:bg-surface-muted pointer-coarse:h-11"
            >
              <ExternalLink className="size-4" aria-hidden />
              Open in Gmail
            </a>
          </div>
        </div>
      )}
    </Modal>
  )
}
