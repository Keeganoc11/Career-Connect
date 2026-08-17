export const STATUSES = [
  'Applied',
  'PhoneScreen',
  'Interview',
  'Offer',
  'Rejected',
  'Ghosted',
  'Withdrawn',
] as const

export type ApplicationStatus = (typeof STATUSES)[number]

export interface StatusChange {
  fromStatus: ApplicationStatus | null
  toStatus: ApplicationStatus
  changedAtUtc: string
  source: string
}

export interface Application {
  id: string
  companyName: string
  roleTitle: string
  jobPostingUrl: string | null
  status: ApplicationStatus
  dateApplied: string
  notes: string | null
  jobDescriptionText: string | null
  createdAtUtc: string
  updatedAtUtc: string
  statusHistory?: StatusChange[] | null
}

export interface ApplicationInput {
  companyName: string
  roleTitle: string
  jobPostingUrl?: string | null
  dateApplied: string
  notes?: string | null
  jobDescriptionText?: string | null
  status?: ApplicationStatus
}

export interface StatusCount {
  status: ApplicationStatus
  count: number
}

export interface Summary {
  total: number
  counts: StatusCount[]
}

export interface ResumeSummary {
  id: string
  label: string
  isActive: boolean
  characterCount: number
  updatedAtUtc: string
}

export interface Resume {
  id: string
  label: string
  content: string
  isActive: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ResumeInput {
  label: string
  content: string
}

export interface SuggestedEdit {
  section: string
  guidance: string
  suggestedText: string
}

export interface MatchResult {
  id: string
  applicationId: string
  resumeId: string
  resumeLabel: string
  score: number
  summary: string
  matchedKeywords: string[]
  missingKeywords: string[]
  suggestions: SuggestedEdit[]
  modelId: string
  createdAtUtc: string
}

export type TailorResumeInput =
  | { mode: 'existing'; resumeId: string; label: string; content: string }
  | { mode: 'new'; label: string; content: string }

export interface GmailConnectionStatus {
  connected: boolean
  connectedEmail?: string
  connectedAtUtc?: string
  lastCheckedAtUtc?: string
}

export interface SuggestedStatusUpdate {
  applicationId: string
  companyName: string
  roleTitle: string
  currentStatus: ApplicationStatus
  suggestedStatus: ApplicationStatus
  reasoning: string
  emailSubject: string
  emailFrom: string
  emailReceivedAtUtc: string
}

export interface SuggestedNewApplication {
  companyName: string
  roleTitle: string
  reasoning: string
  emailSubject: string
  emailFrom: string
  emailReceivedAtUtc: string
}

export interface GmailScanResult {
  statusUpdates: SuggestedStatusUpdate[]
  newApplications: SuggestedNewApplication[]
}

export interface LoginResponse {
  token: string
  email: string
  displayName: string | null
  expiresAtUtc: string
}
