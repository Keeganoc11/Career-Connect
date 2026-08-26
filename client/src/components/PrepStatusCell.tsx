import type { PrepRun } from '../api/types'

interface Props {
  run: PrepRun | undefined
  hasJobDescription: boolean
  onOpen: () => void
}

export function PrepStatusCell({ run, hasJobDescription, onOpen }: Props) {
  if (!hasJobDescription) {
    return (
      <span className="text-sm text-slate-400" title="Add the job description to enable automated prep">
        —
      </span>
    )
  }

  if (!run) {
    return (
      <button
        type="button"
        onClick={onOpen}
        className="rounded-lg border-2 border-brand-200 bg-brand-50 px-3 py-1.5 text-sm font-semibold whitespace-nowrap text-brand-700 transition hover:border-brand-300 hover:bg-brand-100"
      >
        ⚡ Prep
      </button>
    )
  }

  if (run.status === 'Running') {
    return (
      <button
        type="button"
        onClick={onOpen}
        className="inline-flex items-center gap-2 rounded-lg px-2 py-1.5 text-sm font-semibold whitespace-nowrap text-brand-600 transition hover:bg-white"
      >
        <span className="size-4 animate-spin rounded-full border-2 border-brand-200 border-t-brand-600" />
        Prepping…
      </button>
    )
  }

  if (run.status === 'Failed') {
    return (
      <button
        type="button"
        onClick={onOpen}
        title={run.errorMessage ?? undefined}
        className="rounded-lg px-2 py-1.5 text-sm font-semibold whitespace-nowrap text-rose-600 transition hover:bg-rose-50"
      >
        Prep failed
      </button>
    )
  }

  return (
    <button
      type="button"
      onClick={onOpen}
      className={`rounded-lg px-2.5 py-1.5 text-sm font-bold whitespace-nowrap ring-1 ring-inset transition ${
        run.readyToApply
          ? 'bg-emerald-50 text-emerald-700 ring-emerald-600/20 hover:bg-emerald-100'
          : 'bg-amber-50 text-amber-800 ring-amber-600/20 hover:bg-amber-100'
      }`}
    >
      {run.readyToApply ? '✓ Ready to apply' : `Short of target`}
    </button>
  )
}
