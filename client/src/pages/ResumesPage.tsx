import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { Download, MoreHorizontal, Star, Trash2, Upload } from 'lucide-react'
import { api, ApiError } from '../api/client'
import type { Resume, ResumeSummary } from '../api/types'
import { formatRelative } from '../lib/format'
import { errorMessage } from '../lib/errors'
import { useAsyncAction } from '../lib/useAsyncAction'
import {
  Badge,
  Banner,
  Button,
  Card,
  ConfirmDialog,
  EmptyState,
  Field,
  IconButton,
  Input,
  LoadingState,
  Menu,
  PageHeader,
  Textarea,
  toast,
} from '../components/ui'

const MIN_CONTENT = 50
const MAX_UPLOAD_BYTES = 10 * 1024 * 1024

export function ResumesPage({ dataVersion }: { dataVersion: number }) {
  const [resumes, setResumes] = useState<ResumeSummary[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const [editingId, setEditingId] = useState<string | null>(null)
  const [label, setLabel] = useState('')
  const [content, setContent] = useState('')
  /** What's on the server, so "edited" is a comparison rather than a flag. */
  const [saved, setSaved] = useState({ label: '', content: '' })
  /** Read from a PDF with its layout: the text is locked to the file, and tailoring can use it. */
  const [hasLayout, setHasLayout] = useState(false)
  const [extraFacts, setExtraFacts] = useState('')
  const [savedFacts, setSavedFacts] = useState('')
  /** Why an upload's exact format couldn't be kept, shown until another resume is opened. */
  const [layoutWarning, setLayoutWarning] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ResumeSummary | null>(null)
  /** Held while the discard prompt is up, run if the user confirms. */
  const [pending, setPending] = useState<(() => void) | null>(null)

  const editorRef = useRef<HTMLDivElement>(null)
  const fileRef = useRef<HTMLInputElement>(null)

  const save = useAsyncAction()
  const upload = useAsyncAction()
  const deletion = useAsyncAction()
  const download = useAsyncAction()

  const handleError = useCallback((e: unknown) => setError(errorMessage(e)), [])

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
  }, [refresh, dataVersion])

  const dirty = label !== saved.label || content !== saved.content || extraFacts !== savedFacts

  // The editor holds the only copy of unsaved text, so leaving the tab would
  // lose it outright.
  useEffect(() => {
    if (!dirty) return
    const warn = (event: BeforeUnloadEvent) => event.preventDefault()
    window.addEventListener('beforeunload', warn)
    return () => window.removeEventListener('beforeunload', warn)
  }, [dirty])

  /** Anything that would replace the editor's contents goes through here. */
  const guard = (action: () => void) => {
    if (dirty) setPending(() => action)
    else action()
  }

  const load = (resume: Resume) => {
    setEditingId(resume.id)
    setLabel(resume.label)
    setContent(resume.content)
    setSaved({ label: resume.label, content: resume.content })
    setHasLayout(resume.hasLayout)
    setExtraFacts(resume.extraFacts ?? '')
    setSavedFacts(resume.extraFacts ?? '')
    setLayoutWarning(resume.layoutWarning ?? null)
  }

  const startNew = () => {
    setEditingId(null)
    setLabel('')
    setContent('')
    setSaved({ label: '', content: '' })
    setHasLayout(false)
    setExtraFacts('')
    setSavedFacts('')
    setLayoutWarning(null)
  }

  const startEdit = async (id: string) => {
    try {
      load(await api.getResume(id))
      // On a phone the editor sits below the list, so selecting a resume would
      // otherwise look like nothing happened.
      if (window.matchMedia('(max-width: 1023px)').matches) {
        editorRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' })
      }
    } catch (e) {
      handleError(e)
    }
  }

  const handleUpload = (file: File | undefined) => {
    if (!file) return
    if (file.size > MAX_UPLOAD_BYTES) {
      toast.error('That file is too large — please stay under 10 MB.')
      return
    }

    void upload.run(async () => {
      const created = await api.uploadResume(file)
      await refresh()
      // Land in the editor rather than filing it away silently: a PDF whose
      // format couldn't be kept says why there, and extracted text from any
      // other file usually needs a clean-up pass.
      load(created)
      toast.success(`“${created.label}” uploaded.`)
    })
  }

  const submit = (event: FormEvent) => {
    event.preventDefault()
    void save.run(async () => {
      const input = { label: label.trim(), content: content.trim() }
      const textChanged = input.label !== saved.label || input.content !== saved.content
      const result =
        editingId && !textChanged
          ? { id: editingId }
          : editingId
            ? await api.updateResume(editingId, input)
            : await api.createResume(input)
      if (editingId && extraFacts !== savedFacts) {
        try {
          await api.updateResumeExtraFacts(editingId, extraFacts)
        } catch (e) {
          // The resume was deleted somewhere else — another tab, another device —
          // while this editor still had it open.
          if (e instanceof ApiError && e.status === 404) {
            await refresh()
            throw new Error(
              'This resume no longer exists — it was probably deleted in another tab. Open your resume from the list and paste this in again.',
            )
          }
          throw e
        }
      }
      setEditingId(result.id)
      setSaved({ label: input.label, content: input.content })
      setSavedFacts(extraFacts)
      await refresh()
      toast.success('Resume saved.')
    })
  }

  const makeActive = async (resume: ResumeSummary) => {
    try {
      await api.setActiveResume(resume.id)
      await refresh()
      toast.success(`“${resume.label}” is now your active resume.`)
    } catch (e) {
      toast.error(errorMessage(e) ?? 'Could not set that resume active.')
    }
  }

  const confirmDelete = () =>
    void deletion.run(async () => {
      if (!deleteTarget) return
      await api.deleteResume(deleteTarget.id)
      if (editingId === deleteTarget.id) startNew()
      const { label: deleted } = deleteTarget
      setDeleteTarget(null)
      await refresh()
      toast.success(`“${deleted}” deleted.`)
    })

  const trimmed = content.trim().length
  const tooShort = trimmed > 0 && trimmed < MIN_CONTENT

  return (
    <>
      <div className="space-y-4">
        <PageHeader
          title="Resumes"
          description="Your active resume is the base every tailored resume is built from. Upload it as a PDF so tailoring keeps its exact format."
          actions={
            <>
              <input
                ref={fileRef}
                type="file"
                accept=".pdf,.docx"
                className="sr-only"
                onChange={(e) => {
                  const file = e.target.files?.[0]
                  e.target.value = ''
                  guard(() => handleUpload(file))
                }}
              />
              <Button
                icon={<Upload className="size-4" aria-hidden />}
                loading={upload.busy}
                onClick={() => fileRef.current?.click()}
              >
                Upload file
              </Button>
              <Button variant="primary" onClick={() => guard(startNew)}>
                New resume
              </Button>
            </>
          }
        />

        {error && <Banner>{error}</Banner>}
        {upload.error && <Banner>{upload.error}</Banner>}

        <div className="grid gap-4 lg:grid-cols-[320px_1fr]">
          <section aria-label="Saved resumes">
            {loading ? (
              <LoadingState />
            ) : resumes.length === 0 ? (
              <EmptyState
                title="No resumes yet"
                description="Upload your resume as a PDF. Every tailored version is built from it, in its exact format."
              />
            ) : (
              <ul className="space-y-2">
                {resumes.map((resume) => (
                  <ResumeRow
                    key={resume.id}
                    resume={resume}
                    selected={editingId === resume.id}
                    onOpen={() => guard(() => void startEdit(resume.id))}
                    onMakeActive={() => void makeActive(resume)}
                    onDelete={() => setDeleteTarget(resume)}
                  />
                ))}
              </ul>
            )}
          </section>

          <div ref={editorRef}>
            <Card>
              <form onSubmit={submit} className="space-y-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <h2 className="text-base font-semibold text-fg">
                    {editingId ? 'Edit resume' : 'New resume'}
                  </h2>
                  {editingId && hasLayout && (
                    <Button
                      size="sm"
                      icon={<Download className="size-4" aria-hidden />}
                      loading={download.busy}
                      onClick={() => void download.run(() => api.downloadResumePdf(editingId))}
                    >
                      Download PDF
                    </Button>
                  )}
                </div>

                {layoutWarning && <Banner tone="warning">{layoutWarning}</Banner>}
                {download.error && <Banner>{download.error}</Banner>}

                <Field label="Label">
                  {(props) => (
                    <Input
                      {...props}
                      value={label}
                      onChange={(e) => setLabel(e.target.value)}
                      placeholder="e.g. Backend-focused, Feb 2026"
                      required
                      maxLength={200}
                    />
                  )}
                </Field>

                <Field
                  label="Resume text"
                  error={
                    tooShort
                      ? `At least ${MIN_CONTENT} characters are needed to score against a job description.`
                      : null
                  }
                  hint={
                    hasLayout
                      ? 'Read from your PDF, format and all. To change it, edit the original and upload it again.'
                      : `${trimmed.toLocaleString()} characters`
                  }
                >
                  {(props) => (
                    <Textarea
                      {...props}
                      monospace
                      rows={20}
                      value={content}
                      readOnly={hasLayout}
                      onChange={(e) => setContent(e.target.value)}
                      placeholder="Paste the full text of your resume — experience, skills, education."
                      required
                    />
                  )}
                </Field>

                {hasLayout && (
                  <Field
                    label="Extra facts about you"
                    hint="Everything true that doesn't fit on the page — every project, the tools, what you built, the scale. No length limit. Tailoring can draw on these; it never invents anything beyond them."
                  >
                    {(props) => (
                      <Textarea
                        {...props}
                        rows={12}
                        value={extraFacts}
                        onChange={(e) => setExtraFacts(e.target.value)}
                        placeholder="e.g. Used Docker to run Postgres locally for Career Connect. Wrote 60+ xUnit tests for it."
                      />
                    )}
                  </Field>
                )}

                {save.error && <Banner>{save.error}</Banner>}

                <div className="flex justify-end">
                  {/* Disabled until something actually changed, so the button
                      stops inviting saves that would be no-ops. */}
                  <Button
                    variant="primary"
                    type="submit"
                    loading={save.busy}
                    disabled={!dirty || tooShort}
                  >
                    {editingId ? 'Save changes' : 'Save resume'}
                  </Button>
                </div>
              </form>
            </Card>
          </div>
        </div>
      </div>

      {pending && (
        <ConfirmDialog
          title="Discard changes?"
          body="Your edits to this resume haven't been saved and will be lost."
          confirmLabel="Discard"
          onCancel={() => setPending(null)}
          onConfirm={() => {
            pending()
            setPending(null)
          }}
        />
      )}

      {deleteTarget && (
        <ConfirmDialog
          title="Delete resume?"
          body={`This permanently removes “${deleteTarget.label}”. Resumes with existing match scores can't be deleted.`}
          confirmLabel="Delete resume"
          busyLabel="Deleting…"
          busy={deletion.busy}
          error={deletion.error}
          onConfirm={confirmDelete}
          onCancel={() => setDeleteTarget(null)}
        />
      )}
    </>
  )
}

function ResumeRow({
  resume,
  selected,
  onOpen,
  onMakeActive,
  onDelete,
}: {
  resume: ResumeSummary
  selected: boolean
  onOpen: () => void
  onMakeActive: () => void
  onDelete: () => void
}) {
  const menuRef = useRef<HTMLButtonElement>(null)
  const [menuOpen, setMenuOpen] = useState(false)

  return (
    <li
      className={`rounded-surface border bg-surface transition-colors ${
        selected ? 'border-accent' : 'border-line hover:bg-surface-muted'
      }`}
    >
      <div className="flex items-start justify-between gap-2 p-3">
        <button type="button" onClick={onOpen} className="min-w-0 flex-1 rounded-sm text-left">
          <span className="flex items-center gap-2">
            <span className="truncate text-sm font-medium text-fg">{resume.label}</span>
            {resume.isActive && <Badge emphasis="accent">Active</Badge>}
            {resume.hasLayout && <Badge>PDF format</Badge>}
          </span>
          <span className="mt-0.5 block text-xs text-fg-muted">
            {resume.characterCount.toLocaleString()} characters · {formatRelative(resume.updatedAtUtc)}
          </span>
        </button>

        <IconButton
          ref={menuRef}
          label={`Actions for ${resume.label}`}
          icon={<MoreHorizontal className="size-4" aria-hidden />}
          onClick={() => setMenuOpen((v) => !v)}
        />
        {menuOpen && (
          <Menu
            anchorRef={menuRef}
            label={`Actions for ${resume.label}`}
            onClose={() => setMenuOpen(false)}
            items={[
              ...(resume.isActive
                ? []
                : [
                    {
                      key: 'active',
                      label: 'Set as active',
                      icon: <Star className="size-4" aria-hidden />,
                      onSelect: onMakeActive,
                    },
                  ]),
              {
                key: 'delete',
                label: 'Delete resume',
                icon: <Trash2 className="size-4" aria-hidden />,
                destructive: true,
                onSelect: onDelete,
              },
            ]}
          />
        )}
      </div>
    </li>
  )
}
