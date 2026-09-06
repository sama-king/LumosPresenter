import { useEffect, useRef, useState } from 'react'
import Icon from '../../components/Icon'
import { api } from '../../lib/api'
import type { SongSummaryDto } from '../../lib/types'

interface SongLibraryBandProps {
  songs: SongSummaryDto[]
  selectedId: number | null
  onPickSong: (id: number) => void
  onGoLiveSong: (id: number) => void
  onNewSong: () => void
  onSearch: (query: string) => void
  /** Re-fetch the library after an import. */
  onImported: () => void
  onError: (message: string) => void
}

const COLLAPSE_KEY = 'lumos:song-library-collapsed'

/**
 * Bottom band listing the whole song library (the database), mirroring scripture's
 * ResourcesBand. Collapsible; holds the search box, the full list of songs, and the
 * library actions (New Song / import) on the right — so the left panel is free for
 * session history.
 */
export default function SongLibraryBand({
  songs,
  selectedId,
  onPickSong,
  onGoLiveSong,
  onNewSong,
  onSearch,
  onImported,
  onError,
}: SongLibraryBandProps) {
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(COLLAPSE_KEY) === '1')
  const [query, setQuery] = useState('')
  const [importing, setImporting] = useState(false)
  const txtRef = useRef<HTMLInputElement>(null)
  const ewRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    const handle = window.setTimeout(() => onSearch(query.trim()), 200)
    return () => window.clearTimeout(handle)
  }, [query, onSearch])

  const toggle = () => {
    setCollapsed(prev => {
      const next = !prev
      localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0')
      return next
    })
  }

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
    <section className="flex shrink-0 flex-col border-t border-outline-variant bg-surface-container-lowest">
      <button
        type="button"
        onClick={toggle}
        title={collapsed ? 'Show library' : 'Hide library'}
        className="flex items-center justify-between px-gutter py-2 text-on-surface transition-colors hover:bg-surface-container-low"
      >
        <span className="flex items-center gap-2">
          <Icon name="database" size={18} />
          <h2 className="font-mono text-status-label uppercase">Library</h2>
          <span className="ml-2 font-mono text-mono-ui text-on-surface-variant">
            {songs.length} song{songs.length === 1 ? '' : 's'}
          </span>
        </span>
        <Icon name={collapsed ? 'expand_less' : 'expand_more'} size={20} />
      </button>

      {!collapsed && (
        <div className="flex h-44 gap-8 px-gutter pb-4">
          <div className="flex min-w-0 flex-1 flex-col gap-3">
            <input
              value={query}
              onChange={e => setQuery(e.target.value)}
              placeholder="Search the library..."
              spellCheck={false}
              autoComplete="off"
              className="w-full max-w-md rounded border border-outline-variant bg-surface-container-lowest px-3 py-2 text-body-md placeholder:text-slate-muted focus:border-primary focus:outline-none"
            />
            {songs.length === 0 ? (
              <p className="font-mono text-mono-ui italic text-slate-muted">
                No songs yet. Create one or import a .txt / EasyWorship file.
              </p>
            ) : (
              <ul className="panel-scroll grid min-h-0 flex-1 auto-rows-min grid-cols-2 gap-1.5 overflow-y-auto pr-2 lg:grid-cols-3">
                {songs.map(song => {
                  const isActive = song.id === selectedId
                  return (
                    <li key={song.id}>
                      <button
                        type="button"
                        onClick={() => onPickSong(song.id)}
                        onDoubleClick={() => onGoLiveSong(song.id)}
                        title="Click to preview · double-click to send live"
                        className={`flex w-full items-center justify-between rounded border px-3 py-1.5 text-left transition-colors ${
                          isActive
                            ? 'border-primary/40 bg-primary/5'
                            : 'border-outline-variant bg-surface-container-low hover:border-primary/30'
                        }`}
                      >
                        <span className="min-w-0 truncate text-body-md font-semibold text-on-surface">
                          {song.title}
                          {song.author && (
                            <span className="font-normal text-on-surface-variant"> — {song.author}</span>
                          )}
                        </span>
                      </button>
                    </li>
                  )
                })}
              </ul>
            )}
          </div>

          <div className="flex shrink-0 flex-col justify-center gap-2">
            <button
              type="button"
              onClick={onNewSong}
              className="flex items-center gap-2 rounded bg-primary px-4 py-2 font-mono text-status-label uppercase text-navy-deep transition-all hover:brightness-110"
            >
              <Icon name="add" size={18} />
              New Song
            </button>
            <button
              type="button"
              disabled={importing}
              onClick={() => txtRef.current?.click()}
              className="flex items-center gap-2 rounded border border-outline-variant bg-surface-container px-4 py-2 font-mono text-status-label uppercase text-on-surface-variant transition-colors hover:border-primary hover:text-primary disabled:opacity-40"
            >
              <Icon name={importing ? 'progress_activity' : 'upload_file'} size={18} />
              Import .txt
            </button>
            <button
              type="button"
              disabled={importing}
              onClick={() => ewRef.current?.click()}
              title="Import EasyWorship song.db"
              className="flex items-center gap-2 rounded border border-outline-variant bg-surface-container px-4 py-2 font-mono text-status-label uppercase text-on-surface-variant transition-colors hover:border-primary hover:text-primary disabled:opacity-40"
            >
              <Icon name="database" size={18} />
              Import EW
            </button>
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
        </div>
      )}
    </section>
  )
}
