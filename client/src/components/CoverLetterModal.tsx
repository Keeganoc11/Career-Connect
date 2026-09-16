import { useState } from 'react'
import { Sparkles } from 'lucide-react'
import { api } from '../api/client'
import type { Application } from '../api/types'
import { useAsyncAction } from '../lib/useAsyncAction'
import { Button, ConfirmDialog, CopyButton, EmptyState, Modal, toast } from './ui'

interface Props {
  application: Application
  onClose: () => void
  /**
   * Something here was written onto the application. The page reloads, or other
   * windows (Application prep) seed their editors from a stale copy and write
   * the old text back over what was just saved.
   */
  onChanged: () => void
}

/**
 * Split out of the old "AI tools" modal, which put the cover letter and
 * interview prep behind one vague title. Each is now its own entry point named
 * for what it produces.
 */
export function CoverLetterModal({ application, onClose, onChanged }: Props) {
  // Seeded from the application rather than empty: the prep pipeline saves a
  // cover letter there, and starting blank would offer to write a second one
  // and then throw it away.
  const [content, setContent] = useState<string | null>(application.coverLetterText)
  const [confirmingRegenerate, setConfirmingRegenerate] = useState(false)
  const generate = useAsyncAction()

  const write = () =>
    generate.run(async () => {
      const result = await api.generateCoverLetter(application.id)
      setContent(result.content)
      // Persisted where prep writes it, so both doors lead to one letter.
      // Saving writes both documents, so read the tailored resume as it is
      // *now* — the prop can predate edits saved in Application prep, and
      // passing it through would quietly revert them.
      const current = await api.getApplication(application.id)
      await api.saveDocuments(application.id, {
        tailoredResumeText: current.tailoredResumeText,
        coverLetterText: result.content,
      })
      onChanged()
      toast.success('Cover letter saved.')
    })

  return (
    <>
      <Modal
        title="Cover letter"
        description={`${application.companyName} · ${application.roleTitle}`}
        error={generate.error}
        busy={generate.busy}
        onClose={onClose}
        footerStart={
          content && (
            <Button
              icon={<Sparkles className="size-4" aria-hidden />}
              loading={generate.busy}
              onClick={() => setConfirmingRegenerate(true)}
            >
              Regenerate
            </Button>
          )
        }
        footer={
          <Button onClick={onClose} disabled={generate.busy}>
            Close
          </Button>
        }
      >
        {content ? (
          <div className="space-y-3">
            <div className="flex justify-end">
              <CopyButton text={content} />
            </div>
            <p className="whitespace-pre-wrap text-sm leading-relaxed text-fg">{content}</p>
          </div>
        ) : (
          <EmptyState
            title="No cover letter yet"
            description="Written from this posting and your active resume. Review it before sending — it's a draft, not a final letter."
            action={
              <Button
                variant="primary"
                icon={<Sparkles className="size-4" aria-hidden />}
                loading={generate.busy}
                onClick={() => void write()}
              >
                Write cover letter
              </Button>
            }
          />
        )}
      </Modal>

      {confirmingRegenerate && (
        <ConfirmDialog
          title="Regenerate cover letter?"
          body="The letter below will be replaced with a new one, and any edits to it are lost."
          confirmLabel="Regenerate"
          busyLabel="Writing…"
          busy={generate.busy}
          error={generate.error}
          onCancel={() => setConfirmingRegenerate(false)}
          onConfirm={() => {
            void write().then((ok) => {
              if (ok) setConfirmingRegenerate(false)
            })
          }}
        />
      )}
    </>
  )
}
