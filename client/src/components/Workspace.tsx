import { useCallback, useState } from 'react'
import type { SuggestedNewApplication } from '../api/types'
import { intent as makeIntent, type TrackerIntent } from '../lib/trackerIntent'
import { useGmailConnection } from '../lib/useGmailConnection'
import { AppShell, type View } from './AppShell'
import { EmailUpdatesModal } from './EmailUpdatesModal'
import { AgendaPage } from '../pages/AgendaPage'
import { TrackerPage } from '../pages/TrackerPage'
import { ResumesPage } from '../pages/ResumesPage'

/**
 * The signed-in app: navigation, the Gmail connection, and the three pages.
 *
 * All three stay mounted and are hidden rather than unmounted, so a search
 * term, a filter or half-typed resume text survives switching tabs. It also
 * lets the header open a dialog that TrackerPage owns while another page is on
 * screen.
 *
 * App.tsx is left as nothing but the auth gate.
 */
export function Workspace({ onSignOut }: { onSignOut: () => void }) {
  const [view, setView] = useState<View>('agenda')
  const [emailUpdatesOpen, setEmailUpdatesOpen] = useState(false)
  const [trackerIntent, setTrackerIntent] = useState<TrackerIntent | null>(null)
  const [fromSuggestion, setFromSuggestion] = useState<SuggestedNewApplication | null>(null)

  // Bumped whenever something outside a page changes its data — accepting an
  // email update, say — so the page refetches without being remounted.
  const [dataVersion, setDataVersion] = useState(0)
  const bumpData = useCallback(() => setDataVersion((v) => v + 1), [])

  const gmail = useGmailConnection(bumpData)

  const reviewNewApplication = (suggestion: SuggestedNewApplication) => {
    setFromSuggestion(suggestion)
    setTrackerIntent(
      makeIntent({
        kind: 'new',
        prefill: {
          companyName: suggestion.companyName,
          roleTitle: suggestion.roleTitle,
          dateApplied: suggestion.emailReceivedAtUtc.slice(0, 10),
        },
      }),
    )
    setEmailUpdatesOpen(false)
    setView('tracker')
  }

  return (
    <AppShell
      view={view}
      onViewChange={setView}
      gmail={gmail}
      onOpenEmailUpdates={() => setEmailUpdatesOpen(true)}
      onSignOut={onSignOut}
    >
      <div hidden={view !== 'agenda'}>
        <AgendaPage dataVersion={dataVersion} onOpenTracker={() => setView('tracker')} />
      </div>
      <div hidden={view !== 'tracker'}>
        <TrackerPage
          dataVersion={dataVersion}
          gmailStatus={gmail.status}
          intent={trackerIntent}
          onIntentHandled={() => setTrackerIntent(null)}
          onPrefilledSave={() => {
            // The suggestion promised an application, and now one exists — so
            // it's consumed here rather than the moment the form opened, which
            // would have discarded it if the form were cancelled.
            if (fromSuggestion) gmail.consumeNewApplication(fromSuggestion)
            setFromSuggestion(null)
          }}
        />
      </div>
      <div hidden={view !== 'resumes'}>
        <ResumesPage dataVersion={dataVersion} />
      </div>

      {emailUpdatesOpen && (
        <EmailUpdatesModal
          gmail={gmail}
          onAddNewApplication={reviewNewApplication}
          onClose={() => setEmailUpdatesOpen(false)}
        />
      )}
    </AppShell>
  )
}
