import { useCallback, useEffect, useRef, useState } from 'react'
import { api } from '../../lib/api'
import { useServerEvent } from '../../lib/events'
import { usePersistentState } from '../../lib/persistentState'
import {
  isOfflineTranslation,
  type ApiBibleKeyStatus,
  type ChapterDto,
  type ReferenceEvent,
  type SearchResultDto,
  type Translation,
} from '../../lib/types'
import LivePanel from '../../components/live/LivePanel'
import { useLive } from '../../lib/live'
import ContextPreview from './ContextPreview'
import ResourcesBand from './ResourcesBand'
import SearchPanel, { type HistoryItem } from './SearchPanel'
import {
  AUTO_LIVE_THRESHOLD,
  composeLiveItems,
  fromSlide,
  toSlide,
  type QueueItem,
} from './liveComposer'

const ONLINE_SOURCES_KEY = 'lumos:online-sources'

function timeNow() {
  return new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function verseRange(start: number, end: number | null): number[] {
  const last = end ?? start
  return Array.from({ length: last - start + 1 }, (_, i) => start + i)
}

export default function ScripturePage() {
  // The live queue is not this page's to own: one queue is shared with songs and media, so
  // what the panel shows is what the displays show, whichever tab put it there.
  const live = useLive()
  // Server list (re-fetched on mount). Everything below the divider is durable across nav.
  const [translations, setTranslations] = useState<Translation[]>([])
  const [translation, setTranslation] = usePersistentState('scripture.translation', '')
  const [chapter, setChapter] = usePersistentState<ChapterDto | null>('scripture.chapter', null)
  const [selectedVerses, setSelectedVerses] = usePersistentState<number[]>('scripture.selectedVerses', [])
  const [history, setHistory] = usePersistentState<HistoryItem[]>('scripture.history', [])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')
  // The operator's toggle. Online sources are on by default, and the choice survives a
  // reload, so localStorage rather than usePersistentState (which resets on refresh).
  const [onlineWanted, setOnlineWanted] = useState(
    () => localStorage.getItem(ONLINE_SOURCES_KEY) !== '0',
  )
  // Whether an api.bible key is stored on the server. Null while the first fetch is in
  // flight, so the band can stay quiet instead of flashing "no key set".
  const [apiKey, setApiKey] = useState<ApiBibleKeyStatus | null>(null)
  // Online sources need both a key and the toggle: without a key the server has nothing
  // to fetch with, so the translations must not be offered at all.
  const onlineEnabled = apiKey?.configured === true && onlineWanted

  const selectionAnchor = useRef<number | null>(null)
  const lastAutoDisplay = useRef<string>('')
  // Last translation the console synced to (preview + queue). Separate from the
  // `translation` state, which updates optimistically on dropdown change so the
  // select doesn't snap back while the round-trip completes.
  const syncedTranslation = useRef<string>('')
  // switchTranslation is declared further down; the toggle above needs it, so it is
  // reached through a ref rather than reordering the callbacks.
  const switchTranslationRef = useRef<((code: string) => void) | null>(null)
  // Same reason: losing online sources — by the toggle or by the key being removed — has
  // to move the console off a translation it can no longer resolve.
  const fallBackToOfflineRef = useRef<(() => void) | null>(null)

  const toggleOnline = useCallback(
    (enabled: boolean) => {
      setOnlineWanted(enabled)
      localStorage.setItem(ONLINE_SOURCES_KEY, enabled ? '1' : '0')
      if (!enabled) {
        fallBackToOfflineRef.current?.()
      }
    },
    [],
  )

  // Leaving the console on an online translation it can no longer serve would show an
  // empty chapter on the next lookup, so drop to the first offline Bible instead.
  const fallBackToOffline = useCallback(() => {
    const active = translations.find(t => t.id === translation)
    const fallback = translations.find(isOfflineTranslation)
    if (active && !isOfflineTranslation(active) && fallback) {
      switchTranslationRef.current?.(fallback.id)
    }
  }, [translation, translations])
  fallBackToOfflineRef.current = fallBackToOffline

  // The active translation is server-side state that outlives this console: a session can
  // start with an online one selected and no key to serve it (removed on another machine,
  // or a fresh install). Once both the key status and the translation list are known, move
  // off it rather than letting lookups come back empty.
  useEffect(() => {
    if (apiKey !== null && !onlineEnabled && translations.length > 0) {
      fallBackToOfflineRef.current?.()
    }
  }, [apiKey, onlineEnabled, translations])

  const showError = useCallback((message: string) => {
    setError(message)
    window.setTimeout(() => setError(''), 6000)
  }, [])

  useEffect(() => {
    void api
      .getApiBibleKey()
      .then(setApiKey)
      .catch(() => setApiKey({ configured: false, hint: null }))
    void api
      .getTranslations()
      .then(data => {
        setTranslations(data.translations)
        setTranslation(data.current)
        syncedTranslation.current = data.current
      })
      .catch((err: Error) => showError(err.message))
  }, [showError])

  const loadChapter = useCallback(
    (book: string, chapterNumber: number, select: number[], inTranslation?: string) => {
      setLoading(true)
      return api
        .getChapter(book, chapterNumber, inTranslation)
        .then(data => {
          setChapter(data)
          setSelectedVerses(select)
          selectionAnchor.current = select.length > 0 ? select[0] : null
          return data
        })
        .catch((err: Error) => {
          showError(err.message)
          return null
        })
        .finally(() => setLoading(false))
    },
    [showError],
  )

  const recordHistory = useCallback((item: QueueItem) => {
    setHistory(prev =>
      [
        {
          reference: item.reference,
          book: item.book,
          chapter: item.chapter,
          verses: item.verses,
          snippet: item.text,
          translation: item.translation,
          at: timeNow(),
        },
        ...prev,
      ].slice(0, 50),
    )
  }, [])

  // Session history follows what actually went live rather than being written at each call
  // site: a passage reaches the displays from the preview, the live panel, the search results
  // and the voice pipeline, and only the live channel sees all four.
  const lastRecorded = useRef<string | null>(null)
  const liveSlide = live.liveSlide
  useEffect(() => {
    if (!liveSlide || liveSlide.kind !== 'scripture') return
    if (lastRecorded.current === liveSlide.id) return
    lastRecorded.current = liveSlide.id
    const item = fromSlide(liveSlide)
    if (item) recordHistory(item)
  }, [liveSlide, recordHistory])

  /** Composes the selection into queue items and puts the first one on the displays. */
  const goLive = useCallback(
    (
      target: ChapterDto | null = chapter,
      verses: number[] = selectedVerses,
      source: 'manual' | 'auto' = 'manual',
      liveVerse?: number,
    ) => {
      if (!target || verses.length === 0) return
      const items = composeLiveItems(target, verses, source)
      if (items.length === 0) return
      const chosen =
        liveVerse === undefined
          ? items[0]
          : (items.find(item => item.verses.includes(liveVerse)) ?? items[0])
      live.setQueue({
        slides: items.map(toSlide),
        liveId: chosen.id,
        origin: 'scripture',
        title: `${target.book} ${target.chapter}`,
      })
    },
    [chapter, selectedVerses, live],
  )

  const toggleVerse = useCallback((verse: number, extendRange: boolean) => {
    setSelectedVerses(prev => {
      if (extendRange && selectionAnchor.current !== null) {
        const [from, to] = [selectionAnchor.current, verse].sort((a, b) => a - b)
        return verseRange(from, to)
      }
      selectionAnchor.current = verse
      return prev.includes(verse) ? prev.filter(n => n !== verse) : [...prev, verse]
    })
  }, [])

  /** Double-click: this verse alone replaces whatever is live. */
  const replaceLive = useCallback(
    (verse: number) => {
      setSelectedVerses([verse])
      selectionAnchor.current = verse
      goLive(chapter, [verse], 'manual')
    },
    [chapter, goLive],
  )

  /** playlist_add: grow the current live item with this verse (recomposing may split it). */
  const addToLive = useCallback(
    (verse: number) => {
      if (!chapter) return
      const current = live.liveSlide?.kind === 'scripture' ? live.liveSlide : null
      const sameChapter =
        current && current.book === chapter.book && current.chapter === chapter.chapter
      const merged = sameChapter ? [...new Set([...current.verses, verse])] : [verse]
      setSelectedVerses(merged)
      goLive(chapter, merged, 'manual', verse)
    },
    [chapter, live.liveSlide, goLive],
  )

  const navigateChapter = useCallback(
    (direction: -1 | 1) => {
      if (!chapter) return
      const next = chapter.chapter + direction
      if (next < 1 || next > chapter.chapterCount) return
      void loadChapter(chapter.book, next, [])
    },
    [chapter, loadChapter],
  )

  /**
   * Updates the dropdown optimistically and tells the server; the preview/queue
   * sync happens in the `translation` SSE handler below, so this tab, other tabs,
   * and voice-detected switches all follow one path.
   */
  const switchTranslation = useCallback(
    (code: string) => {
      setTranslation(code)
      void api.setTranslation(code).catch((err: Error) => {
        setTranslation(syncedTranslation.current)
        showError(err.message)
      })
    },
    [showError],
  )
  switchTranslationRef.current = switchTranslation

  const pickSearchResult = useCallback(
    (result: SearchResultDto, options?: { goLive?: boolean }) => {
      const select = result.verseStart === null ? [] : verseRange(result.verseStart, result.verseEnd)
      void loadChapter(result.book, result.chapter, select).then(loaded => {
        if (!loaded || !options?.goLive) return
        // Chapter-only references default to verse 1 on the displays.
        const liveVersesToPush = result.verseStart === null ? [1] : select
        goLive(loaded, liveVersesToPush, 'manual')
      })
    },
    [loadChapter, goLive],
  )

  const pickHistory = useCallback(
    (item: HistoryItem) => {
      void loadChapter(item.book, item.chapter, item.verses)
    },
    [loadChapter],
  )

  /**
   * Mirrors a confident detection into the console's queue/history WITHOUT pushing —
   * the backend pipeline already pushed it live (auto-live is server-side so it works
   * from any page). This only keeps the console UI in sync when it happens to be open.
   */
  const reflectAutoLive = useCallback(
    (target: ChapterDto, verses: number[]) => {
      const items = composeLiveItems(target, verses, 'auto')
      if (items.length === 0) return
      live.adoptQueue({
        slides: items.map(toSlide),
        liveId: items[0].id,
        origin: 'scripture',
        title: `${target.book} ${target.chapter}`,
      })
    },
    [live],
  )

  // Voice pipeline detections: steer the preview; mirror confident ones into the queue.
  useServerEvent<ReferenceEvent>('reference', event => {
    const select = event.verseStart === null ? [] : verseRange(event.verseStart, event.verseEnd)
    void loadChapter(event.book, event.chapter, select).then(loaded => {
      if (!loaded || event.verseStart === null) return
      if (event.confidence < AUTO_LIVE_THRESHOLD) return
      if (event.display === lastAutoDisplay.current) return // same detection re-fired
      lastAutoDisplay.current = event.display
      reflectAutoLive(loaded, select)
    })
  })

  // Translation switched — by this tab's dropdown, another tab, or voice detection.
  // The server already re-pushed the live item in the new translation, so this only
  // syncs the console: preview reloads, and the queue re-composes to mirror what the
  // displays now show (no api.goLive here — same reasoning as reflectAutoLive).
  useServerEvent<{ translation: string }>('translation', event => {
    if (event.translation === syncedTranslation.current) return
    syncedTranslation.current = event.translation
    setTranslation(event.translation)
    if (chapter) {
      void loadChapter(chapter.book, chapter.chapter, selectedVerses, event.translation)
    }
    const current = live.liveSlide && fromSlide(live.liveSlide)
    if (!current) return
    void api
      .getChapter(current.book, current.chapter, event.translation)
      .then(fresh => {
        const items = composeLiveItems(fresh, current.verses, current.source)
        if (items.length === 0) return
        const mirrored = items.find(item => item.verses.includes(current.verses[0])) ?? items[0]
        live.adoptQueue({
          slides: items.map(toSlide),
          liveId: mirrored.id,
          origin: 'scripture',
          title: `${fresh.book} ${fresh.chapter}`,
        })
      })
      .catch((err: Error) => showError(err.message))
  })

  // Which verses of the currently-shown chapter are live on the displays.
  const liveItem = live.liveSlide?.kind === 'scripture' ? live.liveSlide : null
  const liveVerses =
    liveItem && chapter && liveItem.book === chapter.book && liveItem.chapter === chapter.chapter
      ? liveItem.verses
      : []

  // With online sources off the api.bible translations stay listed in the Bibles panel
  // (greyed, so the toggle's effect is visible) but leave the picker entirely.
  const selectableTranslations = onlineEnabled
    ? translations
    : translations.filter(isOfflineTranslation)

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex min-h-0 flex-1">
        <SearchPanel
          translations={selectableTranslations}
          currentTranslation={translation}
          history={history}
          onSwitchTranslation={switchTranslation}
          onPickResult={pickSearchResult}
          onPickHistory={pickHistory}
          onError={showError}
        />
        <ContextPreview
          chapter={chapter}
          selectedVerses={selectedVerses}
          liveVerses={liveVerses}
          loading={loading}
          error={error}
          onNavigateChapter={navigateChapter}
          onToggleVerse={toggleVerse}
          onReplaceLive={replaceLive}
          onAddToLive={addToLive}
          onGoLive={() => goLive()}
        />
        <LivePanel />
      </div>
      <ResourcesBand
        translations={translations}
        currentTranslation={translation}
        onSwitchTranslation={switchTranslation}
        onlineEnabled={onlineEnabled}
        onToggleOnline={toggleOnline}
        onlineWanted={onlineWanted}
        apiKey={apiKey}
      />
    </div>
  )
}
