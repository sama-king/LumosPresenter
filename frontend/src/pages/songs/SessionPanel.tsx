import { useRef, useState } from 'react'
import Icon from '../../components/Icon'
import { api } from '../../lib/api'
import type { SessionSong } from './SongsSession'

interface SessionPanelProps {
  session: SessionSong[]
  selectedId: number | null
  onNewSong: () => void
  onPickSession: (id: number) => void
  onGoLiveSession: (id: number) => void
  /** Re-fetch the library after an import. */
  onImported: () => void
  onError: (message: string) => void
}

/**
 * Left column of the songs console — the analog of scripture's SearchPanel. The New Song /
 * Import actions live at the top, and below them the session history: songs sent live this
 * session (whole songs, not individual sections), most recent first.
 */
export default function SessionPanel({
  session,
  selectedId,
  onNewSong,
  onPickSession,
  onGoLiveSession,
  onImported,
  onError,
}: SessionPanelProps) {
  const [importing, setImporting] = useState(false)
  const txtRef = useRef<HTMLInputElement>(null)
  const ewRef = useRef<HTMLInputElement>(null)

  const handleTxt = async (files: File[]) => {
    setImporting(true)
    try {
      const result = await api.importSongTexts(files)
      onImported()
      if (result.errors.length > 0) {
        onError(
          `${result.imported.length} imported, ${result.errors.length} failed: ` +
            result.errors.map(e => `${e.file} (${e.message})`).join(', '),
        )
      }
    } catch (err) {
      onError((err as Error).message)
    } finally {
      setImporting(false)
    }
  }

  const handleEasyWorship = async (file: File) => {
    setImporting(true)
    try {
      const result = await api.importSongEasyWorship(file)
      onImported()
      const parts = [`${result.imported.length} imported`]
      if (result.skipped.length > 0) parts.push(`${result.skipped.length} already in library`)
      if (result.errors.length > 0) parts.push(`${result.errors.length} failed`)
      if (result.skipped.length > 0 || result.errors.length > 0) onError(parts.join(', '))
    } catch (err) {
      onError((err as Error).message)
    } finally {
      setImporting(false)
    }
  }

  return (
    <aside className="flex w-[300px] shrink-0 flex-col border-r border-outline-variant bg-surface-container-low">
      <div className="border-b border-outline-variant p-4">
        <div className="mb-3 flex items-center gap-2 text-on-surface">
          <Icon name="library_music" size={18} />
          <h2 className="font-mono text-status-label uppercase">Songs</h2>
        </div>
        <button
          type="button"
          onClick={onNewSong}
          className="mb-2 flex w-full items-center justify-center gap-2 rounded bg-primary px-3 py-2.5 font-mono text-status-label uppercase text-navy-deep transition-all hover:brightness-110"
        >
          <Icon name="add" size={18} />
          New Song
        </button>
        <div className="flex gap-2">
          <button
            type="button"
            disabled={importing}
            onClick={() => txtRef.current?.click()}
            className="flex flex-1 items-center justify-center gap-2 rounded border border-outline-variant bg-surface-container px-3 py-2 font-mono text-mono-ui uppercase text-on-surface-variant transition-colors hover:border-primary hover:text-primary disabled:opacity-40"
          >
            <Icon name={importing ? 'progress_activity' : 'upload_file'} size={16} />
            .txt
          </button>
          <button
            type="button"
            disabled={importing}
            onClick={() => ewRef.current?.click()}
            title="Import EasyWorship song.db"
            className="flex flex-1 items-center justify-center gap-2 rounded border border-outline-variant bg-surface-container px-3 py-2 font-mono text-mono-ui uppercase text-on-surface-variant transition-colors hover:border-primary hover:text-primary disabled:opacity-40"
          >
            <Icon name="database" size={16} />
            EW
          </button>
        </div>
        <input
          ref={txtRef}
          type="file"
          accept=".txt,text/plain"
          multiple
          className="hidden"
          onChange={e => {
            const files = Array.from(e.target.files ?? [])
            if (files.length > 0) void handleTxt(files)
            e.target.value = ''
          }}
        />
        <input
          ref={ewRef}
          type="file"
          accept=".db"
          className="hidden"
          onChange={e => {
            const file = e.target.files?.[0]
            if (file) void handleEasyWorship(file)
            e.target.value = ''
          }}
        />
      </div>

      <div className="flex min-h-0 flex-1 flex-col p-4">
        <h3 className="mb-3 font-mono text-mono-ui uppercase tracking-widest text-slate-muted">
          Session
        </h3>
        <div className="panel-scroll min-h-0 flex-1 overflow-y-auto">
          {session.length === 0 ? (
            <p className="text-mono-ui font-mono italic text-slate-muted">
              Songs shown live appear here.
            </p>
          ) : (
            <ul className="flex flex-col gap-1">
              {session.map(item => {
                const isSelected = item.id === selectedId
                return (
                  <li key={item.id}>
                    <button
                      type="button"
                      onClick={() => onPickSession(item.id)}
                      onDoubleClick={() => onGoLiveSession(item.id)}
                      title="Click to preview · double-click to send live"
                      className={`w-full rounded px-2 py-2 text-left transition-colors ${
                        isSelected ? 'bg-surface-container-high' : 'hover:bg-surface-container-high'
                      }`}
                    >
                      <span className="flex items-baseline justify-between gap-2">
                        <span className="truncate text-body-md font-semibold text-on-surface">
                          {item.title}
                        </span>
                        <span className="shrink-0 font-mono text-mono-ui text-slate-muted">
                          {item.at}
                        </span>
                      </span>
                      {item.author && (
                        <span className="block truncate font-mono text-mono-ui text-on-surface-variant">
                          {item.author}
                        </span>
                      )}
                    </button>
                  </li>
                )
              })}
            </ul>
          )}
        </div>
      </div>
    </aside>
  )
}
