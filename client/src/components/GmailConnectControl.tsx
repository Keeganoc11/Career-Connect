import type { GmailConnectionStatus } from '../api/types'
import { formatRelative } from '../lib/format'

interface Props {
  status: GmailConnectionStatus | null
  scanning: boolean
  onConnect: () => void
  onScan: () => void
  onDisconnect: () => void
}

export function GmailConnectControl({ status, scanning, onConnect, onScan, onDisconnect }: Props) {
  if (status === null) {
    return null
  }

  if (!status.connected) {
    return (
      <button
        type="button"
        onClick={onConnect}
        className="inline-flex items-center gap-1.5 rounded-lg border-2 border-dashed border-brand-300 bg-brand-50/50 px-3 py-1.5 text-sm font-semibold text-brand-700 transition hover:border-brand-400 hover:bg-brand-50"
      >
        ✉️ Connect Gmail
      </button>
    )
  }

  return (
    <div className="flex items-center gap-2 rounded-lg border border-slate-200 bg-white py-1 pr-1 pl-3">
      <div className="text-sm">
        <span className="font-semibold text-slate-700">{status.connectedEmail}</span>
        <span className="ml-1.5 text-slate-400">
          {status.lastCheckedAtUtc
            ? `checked ${formatRelative(status.lastCheckedAtUtc)}`
            : 'never checked'}
        </span>
      </div>
      <button
        type="button"
        onClick={onScan}
        disabled={scanning}
        className="rounded-md bg-brand-600 px-3 py-1.5 text-sm font-semibold text-white transition hover:bg-brand-700 disabled:opacity-60"
      >
        {scanning ? 'Checking…' : 'Check for updates'}
      </button>
      <button
        type="button"
        onClick={onDisconnect}
        title="Disconnect Gmail"
        aria-label="Disconnect Gmail"
        className="rounded-md px-2 py-1.5 text-sm font-medium text-slate-400 transition hover:bg-slate-100 hover:text-slate-600"
      >
        ✕
      </button>
    </div>
  )
}
