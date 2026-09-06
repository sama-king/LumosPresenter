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
import ContextPreview from './ContextPreview'
import LiveQueue from './LiveQueue'
import ResourcesBand from './ResourcesBand'
import SearchPanel, { type HistoryItem } from './SearchPanel'
import { AUTO_LIVE_THRESHOLD, composeLiveItems, type QueueItem } from './liveComposer'

const ONLINE_SOURCES_KEY = 'lumos:online-sources'

function timeNow() {
  return new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })
}

function verseRange(start: number, end: number | null): number[] {
  const last = end ?? start
  return Array.from({ length: last - start + 1 }, (_, i) => start + i)
}

export default function ScripturePage() {
  // Server list (re-fetched on mount). Everything below the divider is durable across nav.
  const [translations, setTranslations] = useState<Translation[]>([])
  const [translation, setTranslation] = usePersistentState('scripture.translation', '')
  const [chapter, setChapter] = usePersistentState<ChapterDto | null>('scripture.chapter', null)
  const [selectedVerses, setSelectedVerses] = usePersistentState<number[]>('scripture.selectedVerses', [])
  const [queue, setQueue] = usePersistentState<QueueItem[]>('scripture.queue', [])
  const [liveId, setLiveId] = usePersistentState<string | null>('scripture.liveId', null)
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

  /** Sends one queue item to the displays and records it in session history. */
  const pushLive = useCallback(
    (item: QueueItem) => {
      setLiveId(item.id)
      recordHistory(item)
      void api
        .goLive({
          reference: item.reference,
          text: item.text,
          translation: item.translation,
          source: item.source,
          // Structured reference so a translation switch can re-resolve the passage
          // server-side (item.verses is always one contiguous run).
          book: item.book,
          chapter: item.chapter,
          verseStart: item.verses[0],
          verseEnd: item.verses[item.verses.length - 1],
        })
        .catch((err: Error) => showError(err.message))
    },
    [recordHistory, showError],
  )

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
      setQueue(items)
      const live =
        liveVerse === undefined
          ? items[0]
          : (items.find(item => item.verses.includes(liveVerse)) ?? items[0])
      pushLive(live)
    },
    [chapter, selectedVerses, pushLive],
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
      const live = queue.find(item => item.id === liveId)
      const sameChapter = live && live.book === chapter.book && live.chapter === chapter.chapter
      const merged = sameChapter ? [...new Set([...live.verses, verse])] : [verse]
      setSelectedVerses(merged)
      goLive(chapter, merged, 'manual', verse)
    },
    [chapter, queue, liveId, goLive],
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
      setQueue(items)
      setLiveId(items[0].id)
      recordHistory(items[0])
    },
    [recordHistory],
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
    const live = queue.find(item => item.id === liveId)
    if (!live) return
    void api
      .getChapter(live.book, live.chapter, event.translation)
      .then(fresh => {
        const items = composeLiveItems(fresh, live.verses, live.source)
        if (items.length === 0) return
        const mirrored = items.find(item => item.verses.includes(live.verses[0])) ?? items[0]
        setQueue(items)
        setLiveId(mirrored.id)
        recordHistory(mirrored)
      })
      .catch((err: Error) => showError(err.message))
  })

  const clearAll = useCallback(() => {
    setQueue([])
    setLiveId(null)
    void api.clearLive().catch((err: Error) => showError(err.message))
  }, [showError])

  // Which verses of the currently-shown chapter are live on the displays.
  const liveItem = queue.find(item => item.id === liveId)
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
        <LiveQueue
          queue={queue}
          liveId={liveId}
          onPickItem={pushLive}
          onRemoveItem={id => setQueue(prev => prev.filter(item => item.id !== id))}
          onClearAll={clearAll}
        />
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
