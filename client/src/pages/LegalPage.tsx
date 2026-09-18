import { useEffect } from 'react'
import { BrandMark } from '../components/ui'
import { SUPPORT_EMAIL } from '../lib/tiers'

interface Section {
  heading: string
  paragraphs: string[]
  bullets?: string[]
}

interface LegalDocument {
  title: string
  intro: string
  sections: Section[]
}

/** Both documents were last reviewed on this date; shown at the top of the page. */
const LAST_UPDATED = 'September 18, 2026'

const PRIVACY: LegalDocument = {
  title: 'Privacy Policy',
  intro:
    'Career Connect is a job application tracker. This policy explains what it stores, what it sends elsewhere, and how to get rid of it. It is written to be read rather than to be impressive.',
  sections: [
    {
      heading: 'Who runs this',
      paragraphs: [
        `Career Connect is an independent product built and operated by one person. For anything in this policy — questions, requests, complaints — write to ${SUPPORT_EMAIL}.`,
      ],
    },
    {
      heading: 'What is collected',
      paragraphs: ['Only what the product needs to work:'],
      bullets: [
        'Your account: email address, an optional display name, and a password stored as a hash — never the password itself.',
        'What you put in: applications, job descriptions you paste, resumes you upload, cover letters, interview notes and the answers you record.',
        'With Gmail connected: the contents of recent emails are read to recognise application updates. Emails are not copied into the database; what is stored is the update they suggest, such as "this company replied about this role".',
        'With calendar access granted: the interview events this app creates on your calendar, and their identifiers, so it can update or remove them later.',
        'Ordinary server logs, which include IP addresses, kept for debugging and abuse prevention.',
      ],
    },
    {
      heading: 'Artificial intelligence, and what gets sent where',
      paragraphs: [
        'The automated features work by sending your text to Anthropic’s Claude API for processing. That means the resume you upload, the job descriptions you paste, the email text being classified, and the interview notes you write can be sent to Anthropic in order to produce a tailored resume, a score, a cover letter or a debrief.',
        'Anthropic processes that text to return a result and, under its commercial terms, does not use it to train its models. No other third party receives your content.',
        'Nothing is ever sold, rented, or used for advertising.',
      ],
    },
    {
      heading: 'Google user data',
      paragraphs: [
        'Career Connect’s use and transfer of information received from Google APIs adheres to the Google API Services User Data Policy, including the Limited Use requirements.',
        'Gmail access is read-only and is used for one purpose: recognising updates about jobs you are already tracking. Calendar access is limited to events, and is used only to put your interviews on your calendar. Google data is never used for advertising, never sold, and never transferred to anyone else except as required by law.',
        'You can disconnect Gmail at any time from the account menu, which deletes the stored credential. You can also revoke access directly in your Google account settings.',
      ],
    },
    {
      heading: 'How it is stored',
      paragraphs: [
        'Data lives in a Postgres database hosted by Railway on servers in the United States. The connection is encrypted, and the credential that allows Gmail access is encrypted at rest with a separate key.',
        'Signing in stores a session token in your browser’s local storage. There are no advertising cookies and no third-party analytics or tracking scripts.',
      ],
    },
    {
      heading: 'Getting your data out, and deleting it',
      paragraphs: [
        'You can export everything in your account as a single file, and you can delete your account outright, both from the account menu. Deleting removes your applications, resumes, interviews and notes, and revokes any Gmail access at Google. It cannot be undone.',
        'Backups are kept for a short period for disaster recovery, so deleted content may persist in a backup briefly before ageing out.',
      ],
    },
    {
      heading: 'Children',
      paragraphs: ['Career Connect is not intended for anyone under 16, and accounts are not knowingly created for them.'],
    },
    {
      heading: 'Changes',
      paragraphs: [
        'If this policy changes in a way that matters, the date above changes and anyone with an account is told by email before it takes effect.',
      ],
    },
  ],
}

const TERMS: LegalDocument = {
  title: 'Terms of Service',
  intro:
    'The short version: this is a small independent product, you own what you put in it, the automated features can be wrong, and you are responsible for what you send to an employer.',
  sections: [
    {
      heading: 'The service',
      paragraphs: [
        'Career Connect tracks job applications. The free tier is the tracker itself. The Pro tier adds the automated features — resume tailoring, scoring, cover letters, inbox monitoring and interview preparation.',
        'This is early software, run by one person, and is provided as it is. It may change, break, or be unavailable.',
      ],
    },
    {
      heading: 'Your account',
      paragraphs: [
        'Keep your password to yourself; you are responsible for what happens under your account. Give an email address you actually control, since it is the only way to recover access.',
      ],
    },
    {
      heading: 'Your content',
      paragraphs: [
        'Your resume, notes and everything else you add remain yours. You give permission to store and process that content only to provide the service to you — including sending it to the AI provider described in the privacy policy. That permission ends when you delete the content or your account.',
        'Do not upload anything you do not have the right to use, and do not use the service to break the law or to spam employers.',
      ],
    },
    {
      heading: 'What the automated features are, and what they are not',
      paragraphs: [
        'Tailored resumes, scores, cover letters, debriefs and email classification are produced by a language model. They can be wrong, including confidently wrong. Nothing here is career, legal or financial advice.',
        'Read anything before you send it to an employer. You are responsible for every application you submit, and for checking that it is accurate about you.',
      ],
    },
    {
      heading: 'Paid plans',
      paragraphs: [
        'Pro is currently granted by hand and not sold. When paid plans open, the price, billing terms and refund terms will be stated before any payment is taken, and nobody is charged without agreeing first.',
      ],
    },
    {
      heading: 'Ending it',
      paragraphs: [
        'You can stop using the service and delete your account at any time. Accounts that abuse the service, other users, or the AI provider’s terms may be suspended.',
      ],
    },
    {
      heading: 'Liability',
      paragraphs: [
        'To the extent the law allows: the service is provided without warranties, and the operator is not liable for lost opportunities, lost data, or any indirect damages arising from using it. Nothing here limits liability that cannot legally be limited.',
      ],
    },
    {
      heading: 'Governing law',
      paragraphs: [
        'These terms are governed by the laws of the State of Missouri, without regard to its conflict-of-law rules.',
      ],
    },
    {
      heading: 'Questions',
      paragraphs: [`Write to ${SUPPORT_EMAIL}.`],
    },
  ],
}

/**
 * The privacy policy and terms, at real paths rather than in the hash.
 *
 * Google's OAuth consent screen needs a plain URL for the privacy policy, and
 * a link someone pastes to a colleague should open the document rather than
 * the app. Both render from the same structure so they can't drift in style.
 */
export function LegalPage({
  document: which,
  onHome,
}: {
  document: 'privacy' | 'terms'
  onHome: () => void
}) {
  const doc = which === 'privacy' ? PRIVACY : TERMS

  useEffect(() => {
    window.document.title = `${doc.title} · Career Connect`
  }, [doc.title])

  return (
    <div className="min-h-dvh bg-surface">
      <header className="border-b border-line">
        <div className="mx-auto flex h-14 max-w-3xl items-center px-4">
          <button
            type="button"
            onClick={onHome}
            className="flex items-center gap-2 rounded-sm text-sm font-semibold text-fg"
          >
            <BrandMark className="size-6" />
            Career Connect
          </button>
        </div>
      </header>

      <main className="mx-auto max-w-3xl px-4 py-12">
        <h1 className="text-3xl font-semibold text-fg">{doc.title}</h1>
        <p className="mt-1 text-sm text-fg-subtle">Last updated {LAST_UPDATED}</p>
        <p className="mt-5 text-base leading-relaxed text-fg-muted">{doc.intro}</p>

        {doc.sections.map((section) => (
          <section key={section.heading} className="mt-9">
            <h2 className="text-lg font-semibold text-fg">{section.heading}</h2>
            {section.paragraphs.map((paragraph) => (
              <p key={paragraph} className="mt-3 text-base leading-relaxed text-fg-muted">
                {paragraph}
              </p>
            ))}
            {section.bullets && (
              <ul className="mt-3 list-disc space-y-2 pl-5 text-base leading-relaxed text-fg-muted marker:text-fg-subtle">
                {section.bullets.map((bullet) => (
                  <li key={bullet}>{bullet}</li>
                ))}
              </ul>
            )}
          </section>
        ))}

        <p className="mt-12 border-t border-line pt-6 text-sm text-fg-muted">
          {which === 'privacy' ? (
            <a className="font-medium text-accent hover:text-accent-hover" href="/terms">
              Read the terms of service
            </a>
          ) : (
            <a className="font-medium text-accent hover:text-accent-hover" href="/privacy">
              Read the privacy policy
            </a>
          )}
        </p>
      </main>
    </div>
  )
}
