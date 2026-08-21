import type {
  Application,
  ApplicationInput,
  ApplicationStatus,
  CopilotInsights,
  GmailConnectionStatus,
  GmailScanResult,
  InterviewPrep,
  JobPostingExtraction,
  LoginResponse,
  MatchResult,
  Resume,
  ResumeInput,
  ResumeSummary,
  Summary,
} from './types'

const TOKEN_KEY = 'careerconnect.token'
const EMAIL_KEY = 'careerconnect.email'

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
    throw new ApiError(0, 'Can’t reach the API server. Is it running on port 5199?')
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
    throw new ApiError(0, 'Can’t reach the API server. Is it running on port 5199?')
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

  listApplications() {
    return request<Application[]>('/api/applications')
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

  generateInterviewPrep(applicationId: string) {
    return request<InterviewPrep>(`/api/applications/${applicationId}/interview-prep`, {
      method: 'POST',
    })
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

  /** Whatever the last scheduled background scan found, if anything — undefined if nothing's pending. */
  getPendingGmailSuggestions() {
    return request<GmailScanResult | undefined>('/api/gmail/pending-suggestions')
  },

  getCopilotInsights() {
    return request<CopilotInsights>('/api/copilot/analyze', { method: 'POST' })
  },
}
