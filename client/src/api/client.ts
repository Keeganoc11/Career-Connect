import type {
  Agenda,
  Application,
  ApplicationInput,
  ApplicationStatus,
  CopilotInsights,
  GmailConnectionStatus,
  GmailScanResult,
  InterviewEvent,
  InterviewInput,
  InterviewKind,
  InterviewPrep,
  JobPostingExtraction,
  LoginResponse,
  MatchResult,
  PrepRun,
  Resume,
  ResumeInput,
  ResumeSummary,
  Summary,
} from './types'

const TOKEN_KEY = 'careerconnect.token'
const EMAIL_KEY = 'careerconnect.email'

// Local dev runs the API and client as separate processes on different
// ports, so "unreachable" usually means the API terminal isn't running —
// worth saying so. In production they're one deployed service, so the same
// failure means something else, and a port number would just be confusing.
export const UNREACHABLE_MESSAGE = import.meta.env.DEV
  ? 'Can’t reach the API server. Is it running on port 5199?'
  : 'Can’t reach the server right now. Try again in a moment.'

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

export const auth = {
  get token() {
    return localStorage.getItem(TOKEN_KEY)
  },
  get email() {
    return localStorage.getItem(EMAIL_KEY)
  },
  save(login: LoginResponse) {
    localStorage.setItem(TOKEN_KEY, login.token)
    localStorage.setItem(EMAIL_KEY, login.email)
  },
  clear() {
    localStorage.removeItem(TOKEN_KEY)
    localStorage.removeItem(EMAIL_KEY)
  },
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let message = `Request failed (${response.status})`
    try {
      const problem = await response.json()
      if (problem?.title) {
        message = problem.title
        const details = problem.errors
          ? Object.values<string[]>(problem.errors).flat().join(' ')
          : problem.detail
        if (details) {
          message += `: ${details}`
        }
      }
    } catch {
      // Non-JSON error body; keep the generic message.
    }
    throw new ApiError(response.status, message)
  }

  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  headers.set('Content-Type', 'application/json')
  const token = auth.token
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  let response: Response
  try {
    response = await fetch(path, { ...init, headers })
  } catch {
    // fetch only rejects on network-level failure — almost always "the API
    // isn't running". Status 0 marks it as unreachable rather than a real
    // HTTP error, so callers never confuse it with an empty result.
    throw new ApiError(0, UNREACHABLE_MESSAGE)
  }

  return handleResponse<T>(response)
}

/** Multipart upload — no Content-Type header, fetch sets the boundary itself. */
async function requestFile<T>(path: string, formData: FormData): Promise<T> {
  const headers = new Headers()
  const token = auth.token
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  let response: Response
  try {
    response = await fetch(path, { method: 'POST', headers, body: formData })
  } catch {
    throw new ApiError(0, UNREACHABLE_MESSAGE)
  }

  return handleResponse<T>(response)
}

export const api = {
  login(email: string, password: string) {
    return request<LoginResponse>('/api/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    })
  },

  register(email: string, password: string, displayName?: string) {
    return request<LoginResponse>('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify({ email, password, displayName: displayName || undefined }),
    })
  },

  listApplications() {
    return request<Application[]>('/api/applications')
  },

  getApplication(id: string) {
    return request<Application>(`/api/applications/${id}`)
  },

  getSummary() {
    return request<Summary>('/api/applications/summary')
  },

  extractJobPosting(url: string) {
    return request<JobPostingExtraction>('/api/applications/extract-from-url', {
      method: 'POST',
      body: JSON.stringify({ url }),
    })
  },

  createApplication(input: ApplicationInput) {
    return request<Application>('/api/applications', {
      method: 'POST',
      body: JSON.stringify(input),
    })
  },

  updateApplication(id: string, input: ApplicationInput) {
    return request<Application>(`/api/applications/${id}`, {
      method: 'PUT',
      body: JSON.stringify(input),
    })
  },

  updateStatus(id: string, status: ApplicationStatus) {
    return request<Application>(`/api/applications/${id}/status`, {
      method: 'PATCH',
      body: JSON.stringify({ status }),
    })
  },

  deleteApplication(id: string) {
    return request<void>(`/api/applications/${id}`, { method: 'DELETE' })
  },

  listResumes() {
    return request<ResumeSummary[]>('/api/resumes')
  },

  getResume(id: string) {
    return request<Resume>(`/api/resumes/${id}`)
  },

  createResume(input: ResumeInput) {
    return request<Resume>('/api/resumes', {
      method: 'POST',
      body: JSON.stringify(input),
    })
  },

  uploadResume(file: File, label?: string) {
    const formData = new FormData()
    formData.append('file', file)
    if (label?.trim()) {
      formData.append('label', label.trim())
    }
    return requestFile<Resume>('/api/resumes/upload', formData)
  },

  updateResume(id: string, input: ResumeInput) {
    return request<Resume>(`/api/resumes/${id}`, {
      method: 'PUT',
      body: JSON.stringify(input),
    })
  },

  setActiveResume(id: string) {
    return request<Resume>(`/api/resumes/${id}/active`, { method: 'PATCH' })
  },

  deleteResume(id: string) {
    return request<void>(`/api/resumes/${id}`, { method: 'DELETE' })
  },

  /** Latest stored match per application, keyed by application id. */
  listMatches() {
    return request<Record<string, MatchResult>>('/api/applications/matches')
  },

  scoreMatch(applicationId: string) {
    return request<MatchResult>(`/api/applications/${applicationId}/match`, {
      method: 'POST',
    })
  },

  /** Latest prep run per application, keyed by application id. */
  listPrepRuns() {
    return request<Record<string, PrepRun>>('/api/applications/prep-runs')
  },

  /** Kicks off a prep pass. Returns as soon as it's queued — poll getPrepRun for progress. */
  startPrep(applicationId: string) {
    return request<PrepRun>(`/api/applications/${applicationId}/prep`, { method: 'POST' })
  },

  getPrepRun(applicationId: string) {
    return request<PrepRun>(`/api/applications/${applicationId}/prep`)
  },

  saveDocuments(applicationId: string, documents: { tailoredResumeText: string | null; coverLetterText: string | null }) {
    return request<Application>(`/api/applications/${applicationId}/documents`, {
      method: 'PUT',
      body: JSON.stringify(documents),
    })
  },

  getAgenda() {
    return request<Agenda>('/api/agenda')
  },

  listInterviews(applicationId: string) {
    return request<InterviewEvent[]>(`/api/applications/${applicationId}/interviews`)
  },

  createInterview(applicationId: string, input: InterviewInput) {
    return request<InterviewEvent>(`/api/applications/${applicationId}/interviews`, {
      method: 'POST',
      body: JSON.stringify(input),
    })
  },

  updateInterview(interviewId: string, input: InterviewInput) {
    return request<InterviewEvent>(`/api/interviews/${interviewId}`, {
      method: 'PUT',
      body: JSON.stringify(input),
    })
  },

  deleteInterview(interviewId: string) {
    return request<void>(`/api/interviews/${interviewId}`, { method: 'DELETE' })
  },

  /**
   * The .ics as text. Fetched rather than linked: the endpoint is authorized
   * with the bearer token from localStorage, which a plain <a download> can't
   * send — the browser would just get a 401 file.
   */
  async getInterviewIcs(interviewId: string) {
    const headers = new Headers()
    const token = auth.token
    if (token) headers.set('Authorization', `Bearer ${token}`)

    let response: Response
    try {
      response = await fetch(`/api/interviews/${interviewId}.ics`, { headers })
    } catch {
      throw new ApiError(0, UNREACHABLE_MESSAGE)
    }

    if (!response.ok) {
      throw new ApiError(response.status, `Couldn't build the calendar file (${response.status}).`)
    }
    return response.text()
  },

  tailorResume(applicationId: string, resumeContent: string) {
    return request<{ content: string }>(`/api/applications/${applicationId}/tailor-resume`, {
      method: 'POST',
      body: JSON.stringify({ resumeContent }),
    })
  },

  generateCoverLetter(applicationId: string) {
    return request<{ content: string }>(`/api/applications/${applicationId}/cover-letter`, {
      method: 'POST',
    })
  },

  /** Returns the stored prep, generating it only if there isn't one (or `regenerate` forces a fresh pass). */
  generateInterviewPrep(applicationId: string, regenerate = false) {
    return request<InterviewPrep>(
      `/api/applications/${applicationId}/interview-prep${regenerate ? '?regenerate=true' : ''}`,
      { method: 'POST' },
    )
  },

  /** The stored prep without a model call. Undefined when none has been generated. */
  getInterviewPrep(applicationId: string) {
    return request<InterviewPrep | undefined>(`/api/applications/${applicationId}/interview-prep`)
  },

  getGmailStatus() {
    return request<GmailConnectionStatus>('/api/gmail/status')
  },

  async getGmailAuthorizationUrl() {
    const { authorizationUrl } = await request<{ authorizationUrl: string }>('/api/gmail/connect')
    return authorizationUrl
  },

  disconnectGmail() {
    return request<void>('/api/gmail/connection', { method: 'DELETE' })
  },

  scanGmail() {
    return request<GmailScanResult>('/api/gmail/scan', { method: 'POST' })
  },

  /**
   * Applies a scan suggestion. Separate from updateStatus so history records
   * that email drove it. Passing an interview time schedules it in the same
   * action — one review, both outcomes.
   */
  acceptGmailSuggestion(
    applicationId: string,
    status: ApplicationStatus,
    interview?: { interviewAtUtc: string; interviewKind: InterviewKind },
  ) {
    return request<Application>('/api/gmail/suggestions/accept', {
      method: 'POST',
      body: JSON.stringify({ applicationId, status, ...interview }),
    })
  },

  /** Whatever the last scheduled background scan found, if anything — undefined if nothing's pending. */
  getPendingGmailSuggestions() {
    return request<GmailScanResult | undefined>('/api/gmail/pending-suggestions')
  },

  getCopilotInsights() {
    return request<CopilotInsights>('/api/copilot/analyze', { method: 'POST' })
  },
}
