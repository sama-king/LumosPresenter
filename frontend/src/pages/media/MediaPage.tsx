import { useCallback, useEffect, useMemo, useState } from 'react'
import LivePanel from '../../components/live/LivePanel'
import { api } from '../../lib/api'
import { useLive, type LiveAdvance, type LiveSlide } from '../../lib/live'
import { usePersistentState } from '../../lib/persistentState'
import type { MediaLibraryItemDto } from '../../lib/types'
import ConfirmDialog from './ConfirmDialog'
import MediaGalleryBand from './MediaGalleryBand'
import MediaPreview from './MediaPreview'
import SchedulePanel from './SchedulePanel'
import {
  entryMediaIds,
  entryTitle,
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
 *
 * What is LIVE is not this page's state. It belongs to the shared live channel (lib/live.tsx),
 * along with slideshow timing, queue advance and video transport — so a clip sent from here
 * can still be paused from the scripture tab, and so the displays run one clock between them.
 */
export default function MediaPage() {
  const live = useLive()
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

  const [error, setError] = useState('')
  const [confirmingDelete, setConfirmingDelete] = useState<string[] | null>(null)

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

  /**
   * Turns a schedule entry into live-queue slides. The entry's shape decides how the queue
   * advances: a slideshow on a timer, a video queue when each clip ends, a single item not
   * at all.
   */
  const entrySlides = useCallback(
    (entry: ScheduleEntry): { slides: LiveSlide[]; advance: LiveAdvance } => {
      const slides = entryMediaIds(entry).flatMap<LiveSlide>((mediaId, index) => {
        const media = byId.get(mediaId)
        if (!media) return []
        return [
          {
            // Position-qualified so the same file twice in one group stays two rows.
            id: `${entry.id}:${index}:${mediaId}`,
            kind: 'media',
            reference: media.title,
            title: media.title,
            mediaId: media.id,
            mediaKind: media.kind,
          },
        ]
      })
      const advance: LiveAdvance =
        entry.type === 'slideshow'
          ? { mode: 'timer', secondsPerSlide: entry.secondsPerSlide, wrap: true }
          : entry.type === 'queue'
            ? { mode: 'end', wrap: false }
            : { mode: 'manual' }
      return { slides, advance }
    },
    [byId],
  )

  /** Sends a schedule entry live from its first frame. Slideshows start playing immediately. */
  const goLiveEntry = useCallback(
    (entry: ScheduleEntry) => {
      const { slides, advance } = entrySlides(entry)
      if (slides.length === 0) return
      live.setQueue({
        slides,
        origin: 'media',
        title: entryTitle(entry, byId),
        advance,
        autoPlay: true,
      })
    },
    [entrySlides, live, byId],
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

  // Which schedule row to mark live. Slide ids are prefixed with the entry that produced
  // them, so an ad-hoc gallery push (whose entry was never filed) simply matches nothing.
  const liveEntryId =
    live.origin === 'media' && live.liveSlide
      ? (live.liveSlide.id.split(':')[0] ?? null)
      : null

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

  const setSlideSeconds = useCallback(
    (entryId: string, seconds: number) => {
      setSchedule(prev =>
        prev.map(entry =>
          entry.id === entryId && entry.type === 'slideshow'
            ? { ...entry, secondsPerSlide: seconds }
            : entry,
        ),
      )
      // Adjusting the slideshow that is running should take effect now, not on the next
      // go-live: the operator reaches for this control because the pace is wrong on screen.
      if (liveEntryId === entryId) live.setSecondsPerSlide(seconds)
    },
    [liveEntryId, live],
  )

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
          liveEntryId={schedule.some(entry => entry.id === liveEntryId) ? liveEntryId : null}
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
        <LivePanel />
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
