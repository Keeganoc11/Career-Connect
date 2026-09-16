export const STATUSES = [
  'Preparing',
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

export const INTERVIEW_KINDS = ['PhoneScreen', 'Technical', 'Onsite', 'Final', 'Other'] as const

export type InterviewKind = (typeof INTERVIEW_KINDS)[number]

export interface InterviewEvent {
  id: string
  applicationId: string
  scheduledAtUtc: string
  kind: InterviewKind
  notes: string | null
  source: string
  onCalendar: boolean
}

export interface InterviewInput {
  scheduledAtUtc: string
  kind: InterviewKind
  notes?: string
}

export type NudgeKind =
  | 'ReadyToApply'
  | 'NeverPrepped'
  | 'Silent'
  | 'ProbablyGhosted'
  | 'AwaitingYou'

export interface AgendaNudge {
  applicationId: string
  companyName: string
  roleTitle: string
  status: ApplicationStatus
  kind: NudgeKind
  daysSinceActivity: number
  message: string
}

export interface UpcomingInterview {
  interviewId: string
  applicationId: string
  companyName: string
  roleTitle: string
  scheduledAtUtc: string
  kind: InterviewKind
  notes: string | null
  onCalendar: boolean
  hasPrep: boolean
}

export interface Agenda {
  upcomingInterviews: UpcomingInterview[]
  nudges: AgendaNudge[]
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
  tailoredResumeText: string | null
  coverLetterText: string | null
  createdAtUtc: string
  updatedAtUtc: string
  statusHistory?: StatusChange[] | null
  interviews: InterviewEvent[]
}

export type PrepRunStatus = 'Running' | 'Succeeded' | 'Failed'

export interface PrepStep {
  label: string
  detail: string
  score: number | null
}

export interface PrepRun {
  id: string
  applicationId: string
  status: PrepRunStatus
  targetScore: number
  baselineScore: number | null
  finalScore: number | null
  iterations: number
  steps: PrepStep[]
  readyToApply: boolean | null
  /** What this pass was asked to change, if anything. */
  instructions: string | null
  /** The reality check. Null while running, on failure, and on runs from before it existed. */
  review: ResumeReview | null
  /** Lines the tailored resume changed from the base, with why. */
  changes: ResumeChange[]
  errorMessage: string | null
  startedAtUtc: string
  completedAtUtc: string | null
}

export type FitVerdict = 'StrongFit' | 'WorthAShot' | 'Stretch' | 'NotAFit'
export type GapSeverity = 'Dealbreaker' | 'Fixable' | 'Minor'
export type GapFix = 'Resume' | 'Interview' | 'BuildSkill'

export interface ResumeReview {
  verdict: FitVerdict
  realityCheck: string
  scoreCeiling: string
  dealbreakers: { requirement: string; why: string }[]
  strengths: { point: string; evidence: string }[]
  gaps: { requirement: string; severity: GapSeverity; fix: GapFix; advice: string }[]
  workOn: { skill: string; why: string; nextStep: string }[]
}

export interface ResumeChange {
  lineId: string
  before: string
  after: string
  reason: string
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
  /** Read from a PDF with its layout intact — the only kind tailoring can use. */
  hasLayout: boolean
  updatedAtUtc: string
}

export interface Resume {
  id: string
  label: string
  content: string
  isActive: boolean
  hasLayout: boolean
  extraFacts: string | null
  /** Set on upload only: why the file's exact format couldn't be kept. */
  layoutWarning?: string | null
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

export interface CaptureJobInput {
  jobDescriptionText?: string | null
  jobPostingUrl?: string | null
  /** YYYY-MM-DD in the user's own timezone. */
  localDate: string
  allowDuplicate?: boolean
}

export interface CaptureJobResult {
  application: Application
  prepRun: PrepRun | null
  /** Why tailoring didn't start, when it didn't. The application was still saved. */
  prepMessage: string | null
}

export interface GmailConnectionStatus {
  connected: boolean
  connectedEmail?: string
  connectedAtUtc?: string
  lastCheckedAtUtc?: string
  hasPendingSuggestions: boolean
  /** False on connections predating calendar sync — Google can't widen them, so they need reconnecting. */
  calendarEnabled: boolean
}

/** Extra fields carry the interview time a scan read out of the email, when it found one. */
export interface SuggestedStatusUpdate {
  interviewAtUtc?: string | null
  interviewKind?: InterviewKind | null
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

export interface AutoApplied {
  applicationId: string
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
  autoApplied: AutoApplied[]
}

export interface JobPostingExtraction {
  companyName: string
  roleTitle: string
  jobDescriptionText: string
}

export interface CopilotAction {
  title: string
  detail: string
  priority: 'high' | 'medium' | 'low'
  applicationId: string | null
}

export interface CopilotInsights {
  overallSummary: string
  actions: CopilotAction[]
}

export interface InterviewQuestion {
  question: string
  whyItMightComeUp: string
}

export interface TalkingPoint {
  point: string
  howToUseIt: string
}

export interface InterviewPrep {
  questions: InterviewQuestion[]
  talkingPoints: TalkingPoint[]
}

export interface LoginResponse {
  token: string
  email: string
  displayName: string | null
  expiresAtUtc: string
}
