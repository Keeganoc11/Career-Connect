import type { Application, CopilotAction, CopilotInsights } from '../api/types'

interface Props {
  insights: CopilotInsights | null
  loading: boolean
  error: string | null
  applications: Application[]
  onAnalyze: () => void
  onDismiss: () => void
  onOpenApplication: (application: Application) => void
}

const PRIORITY_META: Record<CopilotAction['priority'], { label: string; className: string }> = {
  high: { label: 'High', className: 'bg-rose-50 text-rose-700 ring-rose-600/20' },
  medium: { label: 'Medium', className: 'bg-amber-50 text-amber-700 ring-amber-600/20' },
  low: { label: 'Low', className: 'bg-slate-100 text-slate-600 ring-slate-400/20' },
}

export function CopilotPanel({
  insights,
  loading,
  error,
  applications,
  onAnalyze,
  onDismiss,
  onOpenApplication,
}: Props) {
  if (!insights && !loading && !error) {
    return (
      <button
        type="button"
        onClick={onAnalyze}
        className="inline-flex items-center gap-1.5 self-start rounded-lg border-2 border-dashed border-brand-300 bg-brand-50/50 px-3 py-1.5 text-sm font-semibold text-brand-700 transition hover:border-brand-400 hover:bg-brand-50"
      >
        ✨ Get AI insights
      </button>
    )
  }

  return (
    <div className="rounded-2xl border border-brand-100 bg-brand-50/40 p-5">
      <div className="flex items-start justify-between gap-3">
        <h2 className="text-lg font-bold text-slate-900">✨ AI insights</h2>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onAnalyze}
            disabled={loading}
            className="rounded-lg bg-brand-600 px-3 py-1.5 text-sm font-semibold text-white transition hover:bg-brand-700 disabled:opacity-60"
          >
            {loading ? 'Thinking…' : insights ? 'Refresh' : 'Get insights'}
          </button>
          <button
            type="button"
            onClick={onDismiss}
            aria-label="Dismiss"
            className="rounded-md px-2 py-1.5 text-sm font-medium text-slate-400 transition hover:bg-slate-100 hover:text-slate-600"
          >
            ✕
          </button>
        </div>
      </div>

      {loading && !insights && <p className="mt-3 text-sm text-slate-500">Reviewing your pipeline…</p>}

      {error && <p className="mt-3 text-sm font-medium text-rose-700">{error}</p>}

      {insights && (
        <>
          <p className="mt-3 text-base leading-relaxed text-slate-700">{insights.overallSummary}</p>

          {insights.actions.length > 0 && (
            <ul className="mt-4 space-y-2.5">
              {insights.actions.map((action, index) => {
                const application = action.applicationId
                  ? applications.find((a) => a.id === action.applicationId)
                  : undefined
                const priority = PRIORITY_META[action.priority] ?? PRIORITY_META.low

                return (
                  <li
                    key={index}
                    className="flex items-start gap-3 rounded-xl border border-white bg-white p-3.5 shadow-sm"
                  >
                    <span
                      className={`mt-0.5 shrink-0 rounded-full px-2 py-0.5 text-xs font-bold uppercase ring-1 ring-inset ${priority.className}`}
                    >
                      {priority.label}
                    </span>
                    <div className="min-w-0 flex-1">
                      <p className="font-semibold text-slate-900">{action.title}</p>
                      <p className="mt-0.5 text-sm text-slate-600">{action.detail}</p>
                      {application && (
                        <button
                          type="button"
                          onClick={() => onOpenApplication(application)}
                          className="mt-1.5 text-sm font-semibold text-brand-600 hover:text-brand-700"
                        >
                          {application.companyName} — {application.roleTitle} →
                        </button>
                      )}
                    </div>
                  </li>
                )
              })}
            </ul>
          )}
        </>
      )}
    </div>
  )
}
