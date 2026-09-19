import { useCallback, useEffect, useMemo, useState } from 'react'
import LivePanel from '../../components/live/LivePanel'
import { api } from '../../lib/api'
import { useLive } from '../../lib/live'
import { usePersistentState } from '../../lib/persistentState'
import type { SongDto, SongSummaryDto } from '../../lib/types'
import SectionList from './SectionList'
import SessionPanel from './SessionPanel'
import SongEditor from './SongEditor'
import SongLibraryBand from './SongLibraryBand'
import type { SessionSong } from './SongsSession'
import { composeSongQueue, toSlide } from './songQueue'

/**
 * Songs operator console (/songs). Layout mirrors the scripture page: session history +
 * actions in the left panel, the selected song's sections (or the editor) in the middle,
 * the live section queue on the right, and the whole library in a collapsible bottom band.
 * The selected song, live queue and session history live in SongsSession (above the routes)
 * so they survive navigating away and back. Go-live pushes one section with kind:'song'.
 */
export default function SongsPage() {
  // The live queue is shared with scripture and media — see lib/live.tsx. This page fills it
  // with the sections of whatever song went live and otherwise reads what is live back.
  const live = useLive()
  // Durable across navigation (usePersistentState); transient (loading/editing/error) is not.
  const [selected, setSelected] = usePersistentState<SongDto | null>('songs.selected', null)
  // Section staged in the middle column (single-click select), distinct from what is live.
  const [selectedSection, setSelectedSection] = usePersistentState<number | null>(
    'songs.selectedSection',
    null,
  )
  const [session, setSession] = usePersistentState<SessionSong[]>('songs.session', [])
  const [songs, setSongs] = useState<SongSummaryDto[]>([])
  const [loading, setLoading] = useState(false)
  const [editing, setEditing] = useState(false)
  const [error, setError] = useState('')

  const showError = useCallback((message: string) => {
    setError(message)
    window.setTimeout(() => setError(''), 6000)
  }, [])

  // Records a song in the session history (whole songs, most-recent-first, de-duped). Going live
  // does this, and so does the library's add-to-session button, which lets the operator line up
  // a set before the service without putting anything on the displays.
  const recordSession = useCallback(
    (song: Pick<SongSummaryDto, 'id' | 'title' | 'author'>) =>
      setSession(prev => {
        const at = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
        const withoutSong = prev.filter(s => s.id !== song.id)
        return [{ id: song.id, title: song.title, author: song.author, at }, ...withoutSong].slice(0, 50)
      }),
    [setSession],
  )

  const refreshLibrary = useCallback(
    (query?: string) =>
      api
        .getSongs(query)
        .then(data => setSongs(data.songs))
        .catch((err: Error) => showError(err.message)),
    [showError],
  )

  useEffect(() => {
    void refreshLibrary()
  }, [refreshLibrary])

  // Previewing a song fills only the middle column — never the live queue. The live
  // queue (right) holds the sections of the song that is actually on the displays, and
  // is populated the moment something goes live (see pushLiveFromSong).
  const pickSong = useCallback(
    (id: number) => {
      setLoading(true)
      setEditing(false)
      void api
        .getSong(id)
        .then(song => {
          setSelected(song)
          setSelectedSection(null)
        })
        .catch((err: Error) => showError(err.message))
        .finally(() => setLoading(false))
    },
    [showError, setSelected, setSelectedSection],
  )

  /**
   * Sends one section of `song` live: publishes the live queue (that song's sections),
   * marks the section live, records the session, and pushes to the displays. This is the
   * single choke point that populates the live queue — nothing else does.
   */
  const pushLiveFromSong = useCallback(
    (song: SongDto, position: number) => {
      const items = composeSongQueue(song)
      const item = items.find(q => q.sectionPosition === position)
      if (!item) return
      recordSession(song)
      live.setQueue({
        slides: items.map(queued => toSlide(song, queued)),
        liveId: item.id,
        origin: 'songs',
        title: song.title,
      })
    },
    [live, recordSession],
  )

  /**
   * Loads a song and sends its first section live in one gesture (double-click in the
   * library or session history).
   */
  const goLiveSong = useCallback(
    (id: number) => {
      setLoading(true)
      setEditing(false)
      void api
        .getSong(id)
        .then(song => {
          setSelected(song)
          setSelectedSection(null)
          const first = song.sections[0]
          if (first) pushLiveFromSong(song, first.position)
        })
        .catch((err: Error) => showError(err.message))
        .finally(() => setLoading(false))
    },
    [showError, setSelected, setSelectedSection, pushLiveFromSong],
  )

  // Double-click a section in the middle column: send it live.
  const goLiveSection = useCallback(
    (position: number) => {
      if (selected) pushLiveFromSong(selected, position)
    },
    [selected, pushLiveFromSong],
  )

  // Single-click a section: stage it (blue-selected) without touching the displays.
  const selectSection = useCallback(
    (position: number) => setSelectedSection(position),
    [setSelectedSection],
  )

  // ← / → / Space step through the live song's sections. Ignored while typing in a field, and
  // only while a song is what is live — the same keys must not walk a scripture queue from here.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (live.origin !== 'songs' || live.slides.length === 0) return
      const target = e.target as HTMLElement | null
      if (target && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)) return
      if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft' && e.key !== ' ') return
      e.preventDefault()
      live.step(e.key === 'ArrowLeft' ? -1 : 1)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [live])

  const onSaved = useCallback(
    (song: SongDto) => {
      setSelected(song)
      setEditing(false)
      void refreshLibrary()
    },
    [refreshLibrary, setSelected],
  )

  const onDeleted = useCallback(
    (id: number) => {
      setEditing(false)
      if (selected?.id === id) setSelected(null)
      // If the deleted song is the one on the displays, take it off them: a queue pointing at
      // sections that no longer exist would break the next advance.
      if (live.liveSlide?.kind === 'song' && live.liveSlide.songId === id) {
        live.clear()
      }
      void refreshLibrary()
    },
    [refreshLibrary, selected, live, setSelected],
  )

  const newSong = useCallback(() => {
    setSelected(null)
    setEditing(true)
  }, [setSelected])

  // The section highlight in the middle column only applies when the previewed song IS the
  // one on the displays; another song (or a verse) being live leaves it unmarked.
  const liveSlide = live.liveSlide
  const liveSectionPosition =
    liveSlide?.kind === 'song' && selected && liveSlide.songId === selected.id
      ? liveSlide.sectionPosition
      : null

  const sessionIds = useMemo(() => new Set(session.map(s => s.id)), [session])

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      {error && (
        <div
          className="shrink-0 bg-rose-error/10 px-4 py-1.5 font-mono text-mono-ui text-rose-error"
          role="alert"
        >
          {error}
        </div>
      )}
      <div className="flex min-h-0 flex-1">
        <SessionPanel
          session={session}
          selectedId={selected?.id ?? null}
          onPickSession={pickSong}
          onGoLiveSession={goLiveSong}
        />
        {editing ? (
          <SongEditor
            song={selected}
            onSaved={onSaved}
            onDeleted={onDeleted}
            onCancel={() => setEditing(false)}
            onError={showError}
          />
        ) : (
          <SectionList
            song={selected}
            loading={loading}
            liveSectionPosition={liveSectionPosition}
            selectedSectionPosition={selectedSection}
            onSelectSection={selectSection}
            onGoLiveSection={goLiveSection}
            onEdit={() => setEditing(true)}
          />
        )}
        <LivePanel />
      </div>
      <SongLibraryBand
        songs={songs}
        selectedId={selected?.id ?? null}
        onPickSong={pickSong}
        onGoLiveSong={goLiveSong}
        sessionIds={sessionIds}
        onAddToSession={recordSession}
        onNewSong={newSong}
        onSearch={q => void refreshLibrary(q)}
        onImported={() => void refreshLibrary()}
        onError={showError}
      />
    </div>
  )
}
