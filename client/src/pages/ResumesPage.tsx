import { useCallback, useEffect, useState, type FormEvent } from 'react'
import { api, auth, ApiError } from '../api/client'
import type { ResumeSummary } from '../api/types'
import { formatRelative } from '../lib/format'
import { ConfirmDialog } from '../components/ConfirmDialog'

const inputClass =
  'w-full rounded-xl border border-slate-300 px-4 py-2.5 text-base text-slate-900 placeholder:text-slate-400 focus:border-brand-500 focus:outline-none focus:ring-4 focus:ring-brand-500/15'

const MIN_CONTENT = 50
const MAX_UPLOAD_BYTES = 10 * 1024 * 1024

export function ResumesPage({ onLoggedOut }: { onLoggedOut: () => void }) {
  const [resumes, setResumes] = useState<ResumeSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [editingId, setEditingId] = useState<string | null>(null)
  const [label, setLabel] = useState('')
  const [content, setContent] = useState('')
  const [saving, setSaving] = useState(false)
  const [savedAt, setSavedAt] = useState<number | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ResumeSummary | null>(null)
  const [deleting, setDeleting] = useState(false)
  const [uploading, setUploading] = useState(false)
  const [uploadError, setUploadError] = useState<string | null>(null)

  const handleError = useCallback(
    (e: unknown) => {
      if (e instanceof ApiError && e.status === 401) {
        auth.clear()
        onLoggedOut()
        return
      }
      setError(e instanceof Error ? e.message : 'Something went wrong.')
    },
    [onLoggedOut],
  )

  const refresh = useCallback(async () => {
    try {
      setResumes(await api.listResumes())
      setError(null)
    } catch (e) {
      handleError(e)
    } finally {
      setLoading(false)
    }
  }, [handleError])

  useEffect(() => {
    void refresh()
  }, [refresh])

  const startNew = () => {
    setEditingId(null)
    setLabel('')
    setContent('')
  }

  const startEdit = async (id: string) => {
    try {
      const resume = await api.getResume(id)
      setEditingId(resume.id)
      setLabel(resume.label)
      setContent(resume.content)
    } catch (e) {
      handleError(e)
    }
  }

  const handleUpload = async (file: File | undefined) => {
    if (!file) return
    setUploadError(null)

    if (file.size > MAX_UPLOAD_BYTES) {
      setUploadError('That file is too large — please stay under 10 MB.')
      return
    }

    setUploading(true)
    try {
      const created = await api.uploadResume(file)
      await refresh()
      // Extracted text often needs a quick clean-up pass, so land the user
      // in the editor rather than silently filing it away.
      setEditingId(created.id)
      setLabel(created.label)
      setContent(created.content)
      setSavedAt(Date.now())
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        handleError(e)
      } else {
        setUploadError(e instanceof Error ? e.message : 'Upload failed.')
      }
    } finally {
      setUploading(false)
    }
  }

  const save = async (event: FormEvent) => {
    event.preventDefault()
    setSaving(true)
    setError(null)
    try {
      const input = { label: label.trim(), content: content.trim() }
      const saved = editingId
        ? await api.updateResume(editingId, input)
        : await api.createResume(input)
      setEditingId(saved.id)
      setSavedAt(Date.now())
      await refresh()
    } catch (e) {
      handleError(e)
    } finally {
      setSaving(false)
    }
  }

  const makeActive = async (id: string) => {
    try {
      await api.setActiveResume(id)
      await refresh()
    } catch (e) {
      handleError(e)
    }
  }

  const confirmDelete = async () => {
    if (!deleteTarget) return
    setDeleting(true)
    try {
      await api.deleteResume(deleteTarget.id)
      if (editingId === deleteTarget.id) startNew()
      setDeleteTarget(null)
      await refresh()
    } catch (e) {
      handleError(e)
      setDeleteTarget(null)
    } finally {
      setDeleting(false)
    }
  }

  const trimmed = content.trim().length
  const tooShort = trimmed > 0 && trimmed < MIN_CONTENT
  const justSaved = savedAt !== null && Date.now() - savedAt < 4000

  return (
    <main className="mx-auto max-w-6xl px-5 py-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold tracking-tight text-slate-900">Resumes</h1>
          <p className="mt-1.5 max-w-2xl text-base text-slate-500">
            Upload a PDF or Word doc, or paste the text directly. The one marked{' '}
            <strong className="font-semibold text-brand-700">active</strong> is what new match
            scores are calculated against — keep several versions if you tailor per role.
          </p>
        </div>
        <button
          type="button"
          onClick={startNew}
          className="brand-gradient rounded-xl px-5 py-3 text-base font-semibold text-white shadow-lg shadow-brand-600/25 transition hover:opacity-95"
        >
          + New resume
        </button>
      </div>

      {error && (
        <p className="mt-5 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-base font-medium text-rose-700">
          {error}
        </p>
      )}

      <div className="mt-6 grid gap-6 lg:grid-cols-[340px_1fr]">
        <section aria-label="Saved resumes" className="space-y-3">
          {loading ? (
            <p className="py-8 text-center text-base text-slate-500">Loading…</p>
          ) : resumes.length === 0 ? (
            <p className="rounded-2xl border-2 border-dashed border-slate-300 bg-white p-5 text-base text-slate-500">
              No resumes yet. Paste one on the right to unlock match scoring.
            </p>
          ) : (
            <ul className="space-y-3">
              {resumes.map((resume) => {
                const selected = editingId === resume.id
                return (
                  <li
                    key={resume.id}
                    className={`overflow-hidden rounded-2xl bg-white shadow-sm ring-2 transition ${
                      selected ? 'ring-brand-400' : 'ring-slate-200/70 hover:ring-slate-300'
                    }`}
                  >
                    {resume.isActive && <div className="brand-gradient h-1.5 w-full" aria-hidden />}
                    <div className="p-4">
                      <div className="flex items-start justify-between gap-2">
                        <button
                          type="button"
                          onClick={() => void startEdit(resume.id)}
                          className="flex-1 text-left"
                        >
                          <span className="block text-base font-bold text-slate-900">
                            {resume.label}
                          </span>
                          <span className="mt-0.5 block text-sm text-slate-500">
                            {resume.characterCount.toLocaleString()} characters ·{' '}
                            {formatRelative(resume.updatedAtUtc)}
                          </span>
                        </button>
                        {resume.isActive && (
                          <span className="shrink-0 rounded-full bg-brand-50 px-2.5 py-1 text-xs font-bold uppercase tracking-wide text-brand-700 ring-1 ring-inset ring-brand-200">
                            Active
                          </span>
                        )}
                      </div>
                      <div className="mt-3 flex gap-1">
                        {!resume.isActive && (
                          <button
                            type="button"
                            onClick={() => void makeActive(resume.id)}
                            className="rounded-lg px-3 py-1.5 text-sm font-semibold text-brand-600 transition hover:bg-brand-50"
                          >
                            Make active
                          </button>
                        )}
                        <button
                          type="button"
                          onClick={() => setDeleteTarget(resume)}
                          className="rounded-lg px-3 py-1.5 text-sm font-semibold text-rose-600 transition hover:bg-rose-50"
                        >
                          Delete
                        </button>
                      </div>
                    </div>
                  </li>
                )
              })}
            </ul>
          )}
        </section>

        <form
          onSubmit={save}
          className="overflow-hidden rounded-2xl bg-white shadow-sm ring-1 ring-slate-200"
        >
          <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-6 py-4">
            <h2 className="text-lg font-bold text-slate-900">
              {editingId ? 'Edit resume' : 'New resume'}
            </h2>
            <div className="flex items-center gap-3">
              {justSaved && (
                <span className="rounded-full bg-emerald-50 px-3 py-1 text-sm font-semibold text-emerald-700 ring-1 ring-inset ring-emerald-600/20">
                  Saved
                </span>
              )}
              <label
                className={`inline-flex cursor-pointer items-center gap-1.5 rounded-lg border-2 border-dashed border-brand-300 bg-brand-50/50 px-3 py-1.5 text-sm font-semibold text-brand-700 transition hover:border-brand-400 hover:bg-brand-50 ${
                  uploading ? 'pointer-events-none opacity-60' : ''
                }`}
              >
                {uploading ? 'Reading file…' : 'Upload PDF or Word doc'}
                <input
                  type="file"
                  accept=".pdf,.docx"
                  disabled={uploading}
                  className="sr-only"
                  onChange={(e) => {
                    void handleUpload(e.target.files?.[0])
                    e.target.value = ''
                  }}
                />
              </label>
            </div>
          </div>

          {uploadError && (
            <p className="mx-6 mt-4 rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm font-medium text-rose-700">
              {uploadError}
            </p>
          )}

          <div className="p-6">
            <label className="block">
              <span className="mb-1.5 block text-sm font-semibold text-slate-700">Label *</span>
              <input
                className={inputClass}
                value={label}
                onChange={(e) => setLabel(e.target.value)}
                placeholder="e.g. Backend-focused, Feb 2026"
                required
                maxLength={200}
              />
            </label>

            <label className="mt-5 block">
              <span className="mb-1.5 block text-sm font-semibold text-slate-700">
                Resume text *
              </span>
              <textarea
                className={`${inputClass} font-mono text-sm leading-relaxed`}
                rows={20}
                value={content}
                onChange={(e) => setContent(e.target.value)}
                placeholder="Paste the full text of your resume — experience, skills, education."
                required
              />
            </label>
          </div>

          <div className="flex items-center justify-between gap-3 border-t border-slate-100 bg-slate-50 px-6 py-4">
            <span className={`text-sm font-medium ${tooShort ? 'text-rose-600' : 'text-slate-500'}`}>
              {tooShort
                ? `At least ${MIN_CONTENT} characters needed to score against a job description.`
                : `${trimmed.toLocaleString()} characters`}
            </span>
            <button
              type="submit"
              disabled={saving || tooShort}
              className="brand-gradient rounded-xl px-6 py-2.5 text-base font-semibold text-white shadow-lg shadow-brand-600/25 transition hover:opacity-95 disabled:opacity-50"
            >
              {saving ? 'Saving…' : editingId ? 'Save changes' : 'Save resume'}
            </button>
          </div>
        </form>
      </div>

      {deleteTarget && (
        <ConfirmDialog
          title="Delete resume?"
          body={`This permanently removes “${deleteTarget.label}”. Resumes with existing match scores can't be deleted.`}
          confirmLabel="Delete"
          busy={deleting}
          onConfirm={() => void confirmDelete()}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </main>
  )
}
