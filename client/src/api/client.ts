import type {
  Application,
  ApplicationInput,
  ApplicationStatus,
  LoginResponse,
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

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const headers = new Headers(init?.headers)
  headers.set('Content-Type', 'application/json')
  const token = auth.token
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }

  const response = await fetch(path, { ...init, headers })

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
}
