import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { api } from '../../lib/api'
import { useServerEvent } from '../../lib/events'
import { usePersistentState } from '../../lib/persistentState'
import type { LiveEvent, MediaEndedEvent, MediaLibraryItemDto } from '../../lib/types'
import ConfirmDialog from './ConfirmDialog'
import MediaGalleryBand from './MediaGalleryBand'
import MediaLivePanel from './MediaLivePanel'
import MediaPreview from './MediaPreview'
import SchedulePanel from './SchedulePanel'
import {
  entryMediaIds,
  groupTitle,
  scheduleItem,
  scheduleQueue,
  scheduleSlideshow,
  type ScheduleEntry,
} from './schedule'

const SCHEDULE_KEY = 'lumos:media-schedule'

/**
 * Media operator console (/media). Four panels, matching the scripture tab: schedule on the
 * left, preview in the middle, live on the right, gallery in a collapsible bottom band.
 *
 * The schedule is persisted to localStorage rather than the database: slideshows and queues
 * are service-shaped arrangements the operator builds in the moment, and losing one to an
 * accidental refresh would be worse than the cost of storing a little JSON. It holds gallery
 * ids only, so the gallery (which IS in the database) stays the single source of file truth.
 */
export default function MediaPage() {
  const [items, setItems] = useState<MediaLibraryItemDto[]>([])
  const [schedule, setSchedule] = useState<ScheduleEntry[]>(() => {
    try {
      const raw = localStorage.getItem(SCHEDULE_KEY)
      return raw ? (JSON.parse(raw) as ScheduleEntry[]) : []
    } catch {
      return []
    }
  })

  // Staged (middle column) — either a schedule entry or a bare gallery item, never both.
  const [previewEntryId, setPreviewEntryId] = usePersistentState<string | null>(
    'media.previewEntryId',
    null,
  )
  const [previewItemId, setPreviewItemId] = usePersistentState<string | null>(
    'media.previewItemId',
    null,
  )
  const [selectedIds, setSelectedIds] = usePersistentState<string[]>('media.gallerySelection', [])

  // Live (right column). liveEntryId names a schedule entry, or the synthetic id below when
  // a gallery tile was sent live directly without ever entering the schedule.
  const [liveEntryId, setLiveEntryId] = usePersistentState<string | null>('media.liveEntryId', null)
  const [liveAdHoc, setLiveAdHoc] = usePersistentState<ScheduleEntry | null>('media.liveAdHoc', null)
  const [liveIndex, setLiveIndex] = usePersistentState<number>('media.liveIndex', 0)
  const [playing, setPlaying] = useState(false)
  const [error, setError] = useState('')
  const [confirmingDelete, setConfirmingDelete] = useState<string[] | null>(null)

  // The live-item id of this console's most recent push. Displays report an ended video by
  // that id, and the 'live' SSE echoes it back, so it is how this tab recognises its own work.
  const lastPushedIdRef = useRef<string | null>(null)

  const showError = useCallback((message: string) => {
    setError(message)
    window.setTimeout(() => setError(''), 6000)
  }, [])

  useEffect(() => {
    localStorage.setItem(SCHEDULE_KEY, JSON.stringify(schedule))
  }, [schedule])

  const byId = useMemo(() => new Map(items.map(item => [item.id, item])), [items])

  const refresh = useCallback(
    () =>
      api
        .getMediaLibrary()
        .then(data => setItems(data.items))
        .catch((err: Error) => showError(err.message)),
    [showError],
  )

  useEffect(() => {
    void refresh()
  }, [refresh])

  const liveEntry = useMemo(
    () =>
      liveAdHoc && liveAdHoc.id === liveEntryId
        ? liveAdHoc
        : (schedule.find(entry => entry.id === liveEntryId) ?? null),
    [liveAdHoc, liveEntryId, schedule],
  )
  // Memoized: showIndex closes over this, and the slideshow effect depends on showIndex.
  // A fresh array each render would restart the dwell timer on every render, so a slide
  // could never actually reach its full playtime.
  const liveIds = useMemo(() => (liveEntry ? entryMediaIds(liveEntry) : []), [liveEntry])

  /**
   * Pushes one gallery item to the displays. `position` (1-based) is passed for a group
   * member: it lands in the reference so two identical files in a row are not seen as a
   * duplicate push and suppressed — which would leave a queue stalled on a video that has
   * already ended. `loop` is false for queue members so they can end and report back.
   */
  const pushMedia = useCallback(
    (mediaId: string, options?: { position?: [number, number]; loop?: boolean }) => {
      const media = byId.get(mediaId)
      if (!media) return
      const reference = options?.position
        ? `${media.title} (${options.position[0]}/${options.position[1]})`
        : media.title
      void api
        .goLive({
          reference,
          text: '',
          translation: '',
          kind: 'media',
          mediaId: media.id,
          mediaKind: media.kind,
          mediaLoop: options?.loop ?? true,
        })
        // Remembering the id the server assigned is what lets this console tell its own
        // pushes apart from another tab's, and lets N displays' end-reports collapse to
        // a single advance.
        .then(item => (lastPushedIdRef.current = item.id))
        .catch((err: Error) => showError(err.message))
    },
    [byId, showError],
  )

  /** Sends a schedule entry live from its first frame. Slideshows start playing immediately. */
  const goLiveEntry = useCallback(
    (entry: ScheduleEntry) => {
      const ids = entryMediaIds(entry)
      if (ids.length === 0) return
      // An ad-hoc entry (gallery double-click) is not in the schedule, so keep a copy —
      // otherwise the live panel would have nothing to resolve its id against.
      setLiveAdHoc(schedule.some(e => e.id === entry.id) ? null : entry)
      setLiveEntryId(entry.id)
      setLiveIndex(0)
      setPlaying(entry.type === 'slideshow')
      pushMedia(ids[0], {
        position: entry.type === 'item' ? undefined : [1, ids.length],
        // Queue members must be able to end so the next one can follow.
        loop: entry.type !== 'queue',
      })
    },
    [pushMedia, schedule, setLiveAdHoc, setLiveEntryId, setLiveIndex],
  )

  const goLiveFromSchedule = useCallback(
    (entryId: string) => {
      const entry = schedule.find(e => e.id === entryId)
      if (entry) goLiveEntry(entry)
    },
    [schedule, goLiveEntry],
  )

  const goLiveFromGallery = useCallback(
    (mediaId: string) => goLiveEntry(scheduleItem(mediaId)),
    [goLiveEntry],
  )

  /** Moves the live position within a group and pushes the frame that lands there. */
  const showIndex = useCallback(
    (next: number) => {
      if (liveIds.length === 0) return
      const isQueue = liveEntry?.type === 'queue'
      // A slideshow wraps — it is meant to run unattended. A queue is a sequence with an
      // end: running off it holds the last video rather than starting over.
      if (isQueue && (next < 0 || next >= liveIds.length)) return
      const position = ((next % liveIds.length) + liveIds.length) % liveIds.length
      setLiveIndex(position)
      pushMedia(liveIds[position], {
        position: liveEntry && liveEntry.type !== 'item' ? [position + 1, liveIds.length] : undefined,
        loop: !isQueue,
      })
    },
    [liveEntry, liveIds, pushMedia, setLiveIndex],
  )

  // Slideshow advance. The console owns the timer so what is live is decided in one place;
  // the interval restarts whenever the dwell time or position changes.
  useEffect(() => {
    if (!playing || liveEntry?.type !== 'slideshow' || liveIds.length < 2) return
    const handle = window.setTimeout(
      () => showIndex(liveIndex + 1),
      Math.max(1, liveEntry.secondsPerSlide) * 1000,
    )
    return () => window.clearTimeout(handle)
  }, [playing, liveEntry, liveIndex, liveIds.length, showIndex])

  // Another tab (or the auto-live pipeline) can take the displays at any time. Ownership is
  // decided by live-item id, not by media id and index: during a fast advance this handler
  // can still be holding the previous index, and comparing against it would make the console
  // tear down its own queue mid-run.
  useServerEvent<LiveEvent>('live', event => {
    if (!('cleared' in event) && event.id === lastPushedIdRef.current) return
    lastPushedIdRef.current = null
    setPlaying(false)
    setLiveEntryId(null)
    setLiveAdHoc(null)
    setLiveIndex(0)
  })

  // A queue video finished. Every display showing it reports, so the first report advances
  // (which replaces the tracked id) and the rest fall through as stale — that id check is
  // what keeps a two-display setup from skipping a video per extra screen.
  useServerEvent<MediaEndedEvent>('mediaended', event => {
    if (event.id !== lastPushedIdRef.current) return
    if (liveEntry?.type !== 'queue') return
    lastPushedIdRef.current = null
    showIndex(liveIndex + 1)
  })

  const clearLive = useCallback(() => {
    setPlaying(false)
    setLiveEntryId(null)
    setLiveAdHoc(null)
    setLiveIndex(0)
    void api.clearLive().catch((err: Error) => showError(err.message))
  }, [setLiveEntryId, setLiveAdHoc, setLiveIndex, showError])

  // --- Schedule editing ---

  const addToSchedule = useCallback(
    (mediaIds: string[]) => setSchedule(prev => [...prev, ...mediaIds.map(scheduleItem)]),
    [],
  )

  const createGroup = useCallback(
    (mediaIds: string[], kind: 'slideshow' | 'queue') => {
      if (mediaIds.length === 0) return
      const entry =
        kind === 'slideshow'
          ? scheduleSlideshow(mediaIds, groupTitle(mediaIds, byId, 'Slideshow'))
          : scheduleQueue(mediaIds, groupTitle(mediaIds, byId, 'Queue'))
      // Groups live only in the schedule, so creating one puts it there and stages it,
      // which is also where its playtime / order controls are.
      setSchedule(prev => [...prev, entry])
      setPreviewEntryId(entry.id)
      setPreviewItemId(null)
    },
    [byId, setPreviewEntryId, setPreviewItemId],
  )

  const removeEntry = useCallback(
    (entryId: string) => {
      setSchedule(prev => prev.filter(entry => entry.id !== entryId))
      setPreviewEntryId(current => (current === entryId ? null : current))
      // Removing what is live leaves the displays alone — pulling the picture out from
      // under a service because a row was tidied would be worse than a stale highlight.
    },
    [setPreviewEntryId],
  )

  const reorderSchedule = useCallback((from: number, to: number) => {
    setSchedule(prev => {
      const next = [...prev]
      const [moved] = next.splice(from, 1)
      next.splice(to, 0, moved)
      return next
    })
  }, [])

  const setSlideSeconds = useCallback((entryId: string, seconds: number) => {
    setSchedule(prev =>
      prev.map(entry =>
        entry.id === entryId && entry.type === 'slideshow'
          ? { ...entry, secondsPerSlide: seconds }
          : entry,
      ),
    )
  }, [])

  const reorderGroup = useCallback((entryId: string, from: number, to: number) => {
    setSchedule(prev =>
      prev.map(entry => {
        if (entry.id !== entryId || entry.type === 'item') return entry
        const mediaIds = [...entry.mediaIds]
        const [moved] = mediaIds.splice(from, 1)
        mediaIds.splice(to, 0, moved)
        return { ...entry, mediaIds }
      }),
    )
  }, [])

  const removeFromGroup = useCallback((entryId: string, index: number) => {
    setSchedule(prev =>
      prev.map(entry => {
        if (entry.id !== entryId || entry.type === 'item') return entry
        return { ...entry, mediaIds: entry.mediaIds.filter((_, i) => i !== index) }
      }),
    )
  }, [])

  // --- Gallery ---

  const addPaths = useCallback(
    (paths: string[]) => {
      void api
        .addMediaPaths(paths)
        .then(result => {
          if (result.errors.length > 0) {
            showError(
              `${result.errors.length} file(s) skipped: ${result.errors[0].file} — ${result.errors[0].message}`,
            )
          }
          return refresh()
        })
        .catch((err: Error) => showError(err.message))
    },
    [refresh, showError],
  )

  const deleteFromGallery = useCallback(
    (ids: string[]) => {
      void Promise.all(ids.map(id => api.deleteMediaLibraryItem(id)))
        .then(() => {
          setSelectedIds([])
          setPreviewItemId(current => (current && ids.includes(current) ? null : current))
          return refresh()
        })
        .catch((err: Error) => showError(err.message))
    },
    [refresh, setSelectedIds, setPreviewItemId, showError],
  )

  const previewEntry = schedule.find(entry => entry.id === previewEntryId) ?? null
  const previewItem = previewItemId ? (byId.get(previewItemId) ?? null) : null

  return (
    <div className="flex min-h-0 flex-1 flex-col">
      <div className="flex min-h-0 flex-1">
        <SchedulePanel
          schedule={schedule}
          byId={byId}
          selectedId={previewEntryId}
          liveEntryId={liveEntry && schedule.some(e => e.id === liveEntry.id) ? liveEntry.id : null}
          onPreview={entryId => {
            setPreviewEntryId(entryId)
            setPreviewItemId(null)
          }}
          onGoLive={goLiveFromSchedule}
          onRemove={removeEntry}
          onReorder={reorderSchedule}
        />
        <MediaPreview
          entry={previewEntry}
          item={previewEntry ? null : previewItem}
          byId={byId}
          error={error}
          onGoLive={() => {
            if (previewEntry) goLiveEntry(previewEntry)
            else if (previewItem) goLiveFromGallery(previewItem.id)
          }}
          onSetSlideSeconds={setSlideSeconds}
          onReorderGroup={reorderGroup}
          onRemoveFromGroup={removeFromGroup}
        />
        <MediaLivePanel
          entry={liveEntry}
          ids={liveIds}
          index={Math.min(liveIndex, Math.max(0, liveIds.length - 1))}
          byId={byId}
          playing={playing}
          onSelectIndex={index => {
            setPlaying(false)
            showIndex(index)
          }}
          onTogglePlay={() => setPlaying(prev => !prev)}
          onStep={delta => {
            setPlaying(false)
            showIndex(liveIndex + delta)
          }}
          onClear={clearLive}
        />
      </div>

      <MediaGalleryBand
        items={items}
        selectedIds={selectedIds}
        previewId={previewEntry ? null : previewItemId}
        onSelectionChange={setSelectedIds}
        onPreview={mediaId => {
          setPreviewItemId(mediaId)
          setPreviewEntryId(null)
        }}
        onGoLive={goLiveFromGallery}
        onAddPaths={addPaths}
        onAddToSchedule={addToSchedule}
        onCreateSlideshow={ids => createGroup(ids, 'slideshow')}
        onCreateQueue={ids => createGroup(ids, 'queue')}
        onDelete={setConfirmingDelete}
        onError={showError}
      />

      {confirmingDelete && (
        <ConfirmDialog
          title="Remove from gallery?"
          message={
            confirmingDelete.length === 1
              ? `“${byId.get(confirmingDelete[0])?.title ?? 'This item'}” will be removed from the gallery. The file on disk is not deleted.`
              : `${confirmingDelete.length} items will be removed from the gallery. The files on disk are not deleted.`
          }
          confirmLabel="Remove"
          onConfirm={() => deleteFromGallery(confirmingDelete)}
          onClose={() => setConfirmingDelete(null)}
        />
      )}
    </div>
  )
}
