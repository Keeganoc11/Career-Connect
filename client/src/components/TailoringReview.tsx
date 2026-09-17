import type { ReactNode } from 'react'
import { ArrowRight, Download } from 'lucide-react'
import { api } from '../api/client'
import type { PrepRun, ResumeChange } from '../api/types'
import { FIX_LABELS, SEVERITY_LABELS, VERDICT_LABELS } from '../lib/tailoring'
import { useAsyncAction } from '../lib/useAsyncAction'
import { Badge, Banner, Button, Card } from './ui'

interface Props {
  applicationId: string
  run: PrepRun
  /** Placed right under the verdict — where "ask for changes" belongs, before the detail. */
  afterVerdict?: ReactNode
}

function Section({ title, count, children }: { title: string; count?: number; children: ReactNode }) {
  return (
    <section>
      <h3 className="mb-2 text-sm font-semibold text-fg">
        {title}
        {count !== undefined && <span className="ml-1 font-normal text-fg-muted tabular-nums">({count})</span>}
      </h3>
      {children}
    </section>
  )
}

/**
 * The result of a tailoring pass: the verdict and reality check first, since
 * that decides whether the download is worth using at all, then the file, then
 * the feedback in order of how much it matters.
 */
export function TailoringReview({ applicationId, run, afterVerdict }: Props) {
  const download = useAsyncAction()
  const review = run.review
  if (!review) return null

  return (
    <div className="space-y-5">
      <Card>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <p className="text-xs font-medium uppercase tracking-wide text-fg-muted">Reality check</p>
            <p className="mt-1 text-lg font-semibold text-fg">{VERDICT_LABELS[review.verdict]}</p>
            <p className="mt-0.5 flex items-center gap-1.5 text-sm text-fg-muted tabular-nums">
              Match {run.baselineScore}
              <ArrowRight className="size-3.5" aria-hidden />
              {run.finalScore}
              <span>· bar is {run.targetScore}</span>
            </p>
          </div>
          <Button
            variant="primary"
            icon={<Download className="size-4" aria-hidden />}
            loading={download.busy}
            onClick={() => void download.run(() => api.downloadTailoredResumePdf(applicationId))}
          >
            Download PDF
          </Button>
        </div>
        <p className="mt-3 text-sm leading-relaxed text-fg">{review.realityCheck}</p>
        <p className="mt-2 text-sm leading-relaxed text-fg-muted">{review.scoreCeiling}</p>
        {download.error && (
          <div className="mt-3">
            <Banner>{download.error}</Banner>
          </div>
        )}
      </Card>

      {afterVerdict}

      {review.dealbreakers.length > 0 && (
        <Section title="Dealbreakers" count={review.dealbreakers.length}>
          <ul className="space-y-2">
            {review.dealbreakers.map((d) => (
              <li key={d.requirement} className="rounded-control border border-line px-3.5 py-3">
                <p className="text-sm font-medium text-fg">{d.requirement}</p>
                <p className="mt-0.5 text-sm text-fg-muted">{d.why}</p>
              </li>
            ))}
          </ul>
        </Section>
      )}

      {review.strengths.length > 0 && (
        <Section title="Strengths" count={review.strengths.length}>
          <ul className="space-y-2">
            {review.strengths.map((s) => (
              <li key={s.point} className="rounded-control border border-line px-3.5 py-3">
                <p className="text-sm font-medium text-fg">{s.point}</p>
                <p className="mt-1 border-l-2 border-line pl-2.5 text-sm text-fg-muted">{s.evidence}</p>
              </li>
            ))}
          </ul>
        </Section>
      )}

      {review.gaps.length > 0 && (
        <Section title="Gaps" count={review.gaps.length}>
          <ul className="space-y-2">
            {review.gaps.map((g) => (
              <li key={g.requirement} className="rounded-control border border-line px-3.5 py-3">
                <div className="flex flex-wrap items-center gap-2">
                  <p className="text-sm font-medium text-fg">{g.requirement}</p>
                  <Badge>{SEVERITY_LABELS[g.severity]}</Badge>
                  <span className="text-xs text-fg-muted">{FIX_LABELS[g.fix]}</span>
                </div>
                <p className="mt-1 text-sm text-fg-muted">{g.advice}</p>
              </li>
            ))}
          </ul>
        </Section>
      )}

      {review.workOn.length > 0 && (
        <Section title="What to work on">
          <ul className="space-y-2">
            {review.workOn.map((w) => (
              <li key={w.skill} className="rounded-control border border-line px-3.5 py-3">
                <p className="text-sm font-medium text-fg">{w.skill}</p>
                <p className="mt-0.5 text-sm text-fg-muted">{w.why}</p>
                <p className="mt-1.5 text-sm text-fg">
                  <span className="font-medium">Next step: </span>
                  {w.nextStep}
                </p>
              </li>
            ))}
          </ul>
        </Section>
      )}

      <Section title="What changed" count={run.changes.length}>
        {run.changes.length === 0 ? (
          <p className="text-sm text-fg-muted">
            Nothing — your resume already said this as well as your experience honestly supports.
          </p>
        ) : (
          <ul className="space-y-2">
            {groupChanges(run.changes).map((group) =>
              group.swap ? (
                <li key={group.changes[0].lineId} className="rounded-control border border-line px-3.5 py-3 text-sm">
                  <div className="flex flex-wrap items-center gap-2">
                    <Badge emphasis="accent">Swapped in</Badge>
                    <p className="font-medium text-fg">{group.swap}</p>
                  </div>
                  {group.changes[0].reason && (
                    <p className="mt-1.5 text-xs text-fg-muted">{group.changes[0].reason}</p>
                  )}
                  <ul className="mt-2 space-y-1 border-l-2 border-line pl-3 text-fg">
                    {group.changes.map((c) => (
                      <li key={c.lineId}>{c.after}</li>
                    ))}
                  </ul>
                </li>
              ) : (
                group.changes.map((c) => (
                  <li key={c.lineId} className="rounded-control border border-line px-3.5 py-3 text-sm">
                    <p className="text-fg-subtle line-through">{c.before}</p>
                    <p className="mt-0.5 text-fg">{c.after}</p>
                    {c.reason && <p className="mt-1.5 text-xs text-fg-muted">{c.reason}</p>}
                  </li>
                ))
              ),
            )}
          </ul>
        )}
      </Section>
    </div>
  )
}

/**
 * Every line of a swapped entry arrives as its own change; they read as one
 * swap — the new heading and bullets together, under one reason.
 */
function groupChanges(changes: ResumeChange[]): { swap: string | null; changes: ResumeChange[] }[] {
  const groups: { swap: string | null; changes: ResumeChange[] }[] = []
  for (const change of changes) {
    const group = change.swap ? groups.find((g) => g.swap === change.swap) : undefined
    if (group) group.changes.push(change)
    else groups.push({ swap: change.swap ?? null, changes: [change] })
  }
  return groups
}
