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

export interface LoginResponse {
  token: string
  email: string
  displayName: string | null
  expiresAtUtc: string
}
