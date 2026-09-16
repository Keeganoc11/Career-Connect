import { useCallback, useState } from 'react'
import type { SuggestedNewApplication } from '../api/types'
import { canGoBackInApp, useRoute, type Tab } from '../lib/route'
import {
  intent as makeIntent,
  type TrackerIntent,
  type TrackerIntentRequest,
} from '../lib/trackerIntent'
import { useGmailConnection } from '../lib/useGmailConnection'
import { AppShell } from './AppShell'
import { EmailUpdatesModal } from './EmailUpdatesModal'
import { AgendaPage } from '../pages/AgendaPage'
import { JobPage } from '../pages/JobPage'
import { ResumesPage } from '../pages/ResumesPage'
import { TailorPage } from '../pages/TailorPage'
import { TrackerPage } from '../pages/TrackerPage'

/**
 * The signed-in app: navigation, the Gmail connection, and the pages.
 *
 * The four tab pages stay mounted and are hidden rather than unmounted, so a
 * half-pasted job description, a filter or unsaved resume text survives
 * switching tabs. It also lets TrackerPage's dialogs open over any page.
 *
 * A job's page is the exception: it's a route of its own ("#/jobs/<id>"),
 * mounted when you're on it, so the back button and a reload both work.
 */
export function Workspace({ onSignOut }: { onSignOut: () => void }) {
  const [route, navigate] = useRoute()
  const [emailUpdatesOpen, setEmailUpdatesOpen] = useState(false)
  const [trackerIntent, setTrackerIntent] = useState<TrackerIntent | null>(null)
  const [fromSuggestion, setFromSuggestion] = useState<SuggestedNewApplication | null>(null)

  // Bumped whenever something outside a page changes its data — accepting an
  // email update, say — so the page refetches without being remounted.
  const [dataVersion, setDataVersion] = useState(0)
  const bumpData = useCallback(() => setDataVersion((v) => v + 1), [])

  const gmail = useGmailConnection(bumpData)

  const tab: Tab = route.view === 'job' ? 'tracker' : route.view
  const openTab = (next: Tab) => navigate({ view: next })
  const openJob = useCallback(
    (applicationId: string) => navigate({ view: 'job', applicationId }),
    [navigate],
  )
  // Dialogs open over whatever page is showing; their data lives in TrackerPage.
  const openDialog = useCallback(
    (request: TrackerIntentRequest) => setTrackerIntent(makeIntent(request)),
    [],
  )

  const reviewNewApplication = (suggestion: SuggestedNewApplication) => {
    setFromSuggestion(suggestion)
    openDialog({
      kind: 'new',
      prefill: {
        companyName: suggestion.companyName,
        roleTitle: suggestion.roleTitle,
        dateApplied: suggestion.emailReceivedAtUtc.slice(0, 10),
      },
    })
    setEmailUpdatesOpen(false)
    openTab('tracker')
  }

  const back = () => {
    // Back to wherever you came from when there's in-app history to go back
    // to; a job page opened directly has none, so fall back to the list.
    if (canGoBackInApp()) window.history.back()
    else openTab('tracker')
  }

  return (
    <AppShell
      view={tab}
      ownsTitle={route.view !== 'job'}
      onViewChange={openTab}
      gmail={gmail}
      onOpenEmailUpdates={() => setEmailUpdatesOpen(true)}
      onSignOut={onSignOut}
    >
      {route.view === 'job' && (
        <JobPage
          key={route.applicationId}
          applicationId={route.applicationId}
          dataVersion={dataVersion}
          onBack={back}
          onIntent={openDialog}
          onDataChanged={bumpData}
        />
      )}
      <div hidden={route.view !== 'tailor'}>
        <TailorPage
          dataVersion={dataVersion}
          onOpenJob={openJob}
          onGoToResumes={() => openTab('resumes')}
          onDataChanged={bumpData}
        />
      </div>
      <div hidden={route.view !== 'agenda'}>
        <AgendaPage
          dataVersion={dataVersion}
          onOpenJob={openJob}
          onIntent={openDialog}
          onDataChanged={bumpData}
        />
      </div>
      <div hidden={route.view !== 'tracker'}>
        <TrackerPage
          dataVersion={dataVersion}
          gmailStatus={gmail.status}
          intent={trackerIntent}
          onIntentHandled={() => setTrackerIntent(null)}
          onOpenJob={openJob}
          onDataChanged={bumpData}
          onDeleted={(applicationId) => {
            bumpData()
            // Deleted from its own page: replace that history entry, so Back
            // doesn't return to a page for a job that no longer exists.
            if (route.view === 'job' && route.applicationId === applicationId) {
              window.location.replace('#/applications')
            }
          }}
          onPrefilledSave={() => {
            // The suggestion promised an application, and now one exists — so
            // it's consumed here rather than the moment the form opened, which
            // would have discarded it if the form were cancelled.
            if (fromSuggestion) gmail.consumeNewApplication(fromSuggestion)
            setFromSuggestion(null)
          }}
        />
      </div>
      <div hidden={route.view !== 'resumes'}>
        <ResumesPage dataVersion={dataVersion} />
      </div>

      {emailUpdatesOpen && (
        <EmailUpdatesModal
          gmail={gmail}
          onAddNewApplication={reviewNewApplication}
          dataVersion={dataVersion}
          onDataChanged={bumpData}
          onOpenJob={openJob}
          onClose={() => setEmailUpdatesOpen(false)}
        />
      )}
    </AppShell>
  )
}
