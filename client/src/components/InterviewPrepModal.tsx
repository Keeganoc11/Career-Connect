import { useEffect, useState } from 'react'
import { Sparkles } from 'lucide-react'
import { api } from '../api/client'
import type { Application, InterviewPrep } from '../api/types'
import { useAsyncAction } from '../lib/useAsyncAction'
import { Button, Card, ConfirmDialog, EmptyState, LoadingState, Modal, toast } from './ui'

interface Props {
  application: Application
  onClose: () => void
}

/** Never shortened to "prep" — that's Application prep, which is a different thing. */
export function InterviewPrepModal({ application, onClose }: Props) {
  const [prep, setPrep] = useState<InterviewPrep | null>(null)
  const [loading, setLoading] = useState(true)
  const [confirmingRegenerate, setConfirmingRegenerate] = useState(false)
  const generate = useAsyncAction()

  // Prep is stored once generated — show it rather than making them pay for it
  // again on the morning of the interview.
  useEffect(() => {
    let cancelled = false
    void (async () => {
      try {
        const stored = await api.getInterviewPrep(application.id)
        if (!cancelled && stored) setPrep(stored)
      } catch {
        // Nothing stored yet is the common case; the empty state covers it.
      } finally {
        if (!cancelled) setLoading(false)
      }
    })()
    return () => {
      cancelled = true
    }
  }, [application.id])

  const write = () =>
    generate.run(async () => {
      setPrep(await api.generateInterviewPrep(application.id, prep !== null))
      toast.success('Interview prep ready.')
    })

  return (
    <>
      <Modal
        title="Interview prep"
        description={`${application.companyName} · ${application.roleTitle}`}
        error={generate.error}
        busy={generate.busy}
        onClose={onClose}
        footerStart={
          prep && (
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
        {loading ? (
          <LoadingState />
        ) : prep ? (
          <div className="space-y-6">
            <section>
              <h3 className="text-sm font-semibold text-fg">Likely questions</h3>
              <div className="mt-2 space-y-2">
                {prep.questions.map((question, index) => (
                  <Card key={index}>
                    <p className="text-sm font-medium text-fg">{question.question}</p>
                    <p className="mt-1 text-sm text-fg-muted">{question.whyItMightComeUp}</p>
                  </Card>
                ))}
              </div>
            </section>
            <section>
              <h3 className="text-sm font-semibold text-fg">Talking points</h3>
              <div className="mt-2 space-y-2">
                {prep.talkingPoints.map((point, index) => (
                  <Card key={index}>
                    <p className="text-sm font-medium text-fg">{point.point}</p>
                    <p className="mt-1 text-sm text-fg-muted">{point.howToUseIt}</p>
                  </Card>
                ))}
              </div>
            </section>
          </div>
        ) : (
          <EmptyState
            title="No interview prep yet"
            description="Questions this company is likely to ask, and the parts of your background worth bringing up."
            action={
              <Button
                variant="primary"
                icon={<Sparkles className="size-4" aria-hidden />}
                loading={generate.busy}
                onClick={() => void write()}
              >
                Generate prep
              </Button>
            }
          />
        )}
      </Modal>

      {confirmingRegenerate && (
        <ConfirmDialog
          title="Regenerate interview prep?"
          body="The questions and talking points below will be replaced with a fresh set."
          confirmLabel="Regenerate"
          busyLabel="Thinking…"
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
