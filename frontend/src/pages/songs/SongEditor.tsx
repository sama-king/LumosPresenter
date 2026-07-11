import { useState } from 'react'
import Icon from '../../components/Icon'
import { api } from '../../lib/api'
import type { SongDto } from '../../lib/types'
import { previewSections, sectionsToLyrics } from './songQueue'

interface SongEditorProps {
  /** The song being edited, or null for a new song. */
  song: SongDto | null
  onSaved: (song: SongDto) => void
  onDeleted: (id: number) => void
  onCancel: () => void
  onError: (message: string) => void
}

/**
 * In-app song editor: title/author/copyright plus a lyrics textarea. A blank line
 * starts a new section (slide) — the same rule the server parser applies on save —
 * and the live preview on the right shows how the lyrics will slice into slides.
 */
export default function SongEditor({ song, onSaved, onDeleted, onCancel, onError }: SongEditorProps) {
  const [title, setTitle] = useState(song?.title ?? '')
  const [author, setAuthor] = useState(song?.author ?? '')
  const [copyright, setCopyright] = useState(song?.copyright ?? '')
  const [lyrics, setLyrics] = useState(song ? sectionsToLyrics(song.sections) : '')
  const [saving, setSaving] = useState(false)

  const slides = previewSections(lyrics)
  const canSave = title.trim().length > 0 && slides.length > 0 && !saving

  const save = async () => {
    setSaving(true)
    try {
      const body = { title: title.trim(), author: author.trim(), copyright: copyright.trim(), lyrics }
      const saved = song ? await api.updateSong(song.id, body) : await api.createSong(body)
      onSaved(saved)
    } catch (err) {
      onError((err as Error).message)
    } finally {
      setSaving(false)
    }
  }

  const remove = async () => {
    if (!song) return
    if (!window.confirm(`Delete “${song.title}”? This cannot be undone.`)) return
    try {
      await api.deleteSong(song.id)
      onDeleted(song.id)
    } catch (err) {
      onError((err as Error).message)
    }
  }

  return (
    <section className="flex min-w-0 flex-1 flex-col bg-surface">
      <div className="flex h-14 shrink-0 items-center justify-between border-b border-outline-variant px-4">
        <div className="flex items-center gap-2 text-on-surface">
          <Icon name="edit_note" size={18} />
          <h2 className="font-mono text-status-label uppercase">{song ? 'Edit Song' : 'New Song'}</h2>
        </div>
        <div className="flex items-center gap-3">
          {song && (
            <button
              type="button"
              onClick={remove}
              className="flex items-center gap-2 rounded border border-outline-variant px-3 py-1.5 font-mono text-status-label uppercase text-rose-error transition-colors hover:border-rose-error"
            >
              <Icon name="delete" size={18} />
              Delete
            </button>
          )}
          <button
            type="button"
            onClick={onCancel}
            className="rounded border border-outline-variant px-3 py-1.5 font-mono text-status-label uppercase text-on-surface-variant transition-colors hover:text-on-surface"
          >
            Cancel
          </button>
          <button
            type="button"
            onClick={save}
            disabled={!canSave}
            className="flex items-center gap-2 rounded bg-primary px-4 py-1.5 font-mono text-status-label uppercase text-navy-deep transition-all hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-30"
          >
            <Icon name={saving ? 'progress_activity' : 'save'} size={18} />
            Save
          </button>
        </div>
      </div>

      <div className="grid min-h-0 flex-1 grid-cols-[1fr_260px] gap-0">
        <div className="panel-scroll min-h-0 overflow-y-auto p-6">
          <div className="mx-auto flex max-w-2xl flex-col gap-4">
            <input
              value={title}
              onChange={e => setTitle(e.target.value)}
              placeholder="Song title"
              className="w-full rounded border border-outline-variant bg-surface-container-lowest px-3 py-2.5 text-headline-md font-semibold text-on-surface placeholder:text-slate-muted focus:border-primary focus:outline-none"
            />
            <div className="flex gap-4">
              <input
                value={author}
                onChange={e => setAuthor(e.target.value)}
                placeholder="Author (optional)"
                className="w-full rounded border border-outline-variant bg-surface-container-lowest px-3 py-2 text-body-md placeholder:text-slate-muted focus:border-primary focus:outline-none"
              />
              <input
                value={copyright}
                onChange={e => setCopyright(e.target.value)}
                placeholder="Copyright (optional)"
                className="w-full rounded border border-outline-variant bg-surface-container-lowest px-3 py-2 text-body-md placeholder:text-slate-muted focus:border-primary focus:outline-none"
              />
            </div>
            <textarea
              value={lyrics}
              onChange={e => setLyrics(e.target.value)}
              placeholder={
                'Lyrics — a blank line starts a new slide.\n\nVerse 1\nType each verse as a block...\n\nChorus\n...and label it on its own line if you like.'
              }
              spellCheck={false}
              className="min-h-[420px] w-full flex-1 resize-none rounded border border-outline-variant bg-surface-container-lowest px-3 py-2.5 font-mono text-body-md leading-relaxed text-on-surface placeholder:text-slate-muted focus:border-primary focus:outline-none"
            />
          </div>
        </div>

        <div className="min-h-0 border-l border-outline-variant bg-surface-container-low">
          <div className="panel-scroll h-full overflow-y-auto p-4">
            <h3 className="mb-3 font-mono text-mono-ui uppercase tracking-widest text-slate-muted">
              {slides.length} Slide{slides.length === 1 ? '' : 's'}
            </h3>
            <ul className="flex flex-col gap-2">
              {slides.map((slide, i) => (
                <li
                  key={i}
                  className="rounded border border-outline-variant bg-surface-container p-2.5 font-mono text-mono-ui leading-relaxed text-on-surface-variant"
                >
                  <span className="mb-1 block text-[10px] uppercase tracking-widest text-slate-muted">
                    Slide {i + 1}
                  </span>
                  <span className="line-clamp-4 whitespace-pre-line">{slide}</span>
                </li>
              ))}
            </ul>
          </div>
        </div>
      </div>
    </section>
  )
}
