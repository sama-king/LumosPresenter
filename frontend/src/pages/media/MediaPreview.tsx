import { useRef, useState } from 'react'
import Icon from '../../components/Icon'
import { mediaLibraryFileUrl } from '../../lib/media'
import type { MediaLibraryItemDto } from '../../lib/types'
import MediaThumb from '../../components/MediaThumb'
import {
  entryMediaIds,
  entryTitle,
  MAX_SLIDE_SECONDS,
  MIN_SLIDE_SECONDS,
  type ScheduleEntry,
} from './schedule'

interface MediaPreviewProps {
  /** The schedule entry being previewed, or null when a bare gallery item is staged. */
  entry: ScheduleEntry | null
  /** Staged gallery item when previewing straight from the gallery (no schedule entry). */
  item: MediaLibraryItemDto | null
  byId: Map<string, MediaLibraryItemDto>
  error: string
  onGoLive: () => void
  onSetSlideSeconds: (entryId: string, seconds: number) => void
  onReorderGroup: (entryId: string, fromIndex: number, toIndex: number) => void
  onRemoveFromGroup: (entryId: string, index: number) => void
}

/**
 * Middle column: what is staged, not what is live. Three shapes of content, and the
 * per-type controls the spec puts here rather than in a settings dialog:
 *
 *  - a single item   → just the picture or video
 *  - a slideshow     → contact sheet + the playtime control (seconds per slide)
 *  - a video queue   → the play order, reorderable by drag
 */
export default function MediaPreview({
  entry,
  item,
  byId,
  error,
  onGoLive,
  onSetSlideSeconds,
  onReorderGroup,
  onRemoveFromGroup,
}: MediaPreviewProps) {
  const dragIndex = useRef<number | null>(null)
  const [overIndex, setOverIndex] = useState<number | null>(null)

  const ids = entry ? entryMediaIds(entry) : item ? [item.id] : []
  const first = byId.get(ids[0])
  const title = entry ? entryTitle(entry, byId) : (item?.title ?? '')
  const canGoLive = ids.length > 0

  /** Full-bleed render of one file — the closest thing to what the displays will show. */
  const stage = (media: MediaLibraryItemDto | undefined) => {
    if (!media || !media.exists) {
      return (
        <div className="flex flex-col items-center gap-2 text-rose-error">
          <Icon name="broken_image" size={36} />
          <p className="font-mono text-mono-ui">
            {media ? 'This file is no longer at its saved location.' : 'Nothing staged.'}
          </p>
          {media && (
            <p className="max-w-lg break-all text-center font-mono text-[10px] text-slate-muted">
              {media.sourcePath}
            </p>
          )}
        </div>
      )
    }
    if (media.kind === 'video') {
      return (
        <video
          key={media.id}
          src={mediaLibraryFileUrl(media.id)}
          controls
          muted
          className="max-h-full max-w-full rounded"
        />
      )
    }
    return (
      <img
        key={media.id}
        src={mediaLibraryFileUrl(media.id)}
        alt={media.title}
        className="max-h-full max-w-full rounded object-contain"
      />
    )
  }

  return (
    <section className="flex min-w-0 flex-1 flex-col bg-surface">
      <div className="flex h-14 shrink-0 items-center justify-between border-b border-outline-variant px-4">
        <div className="flex min-w-0 items-center gap-2 text-on-surface">
          <Icon name="preview" size={18} />
          <h2 className="font-mono text-status-label uppercase">Preview</h2>
          {title && (
            <span className="ml-2 truncate font-mono text-mono-ui text-on-surface-variant">
              {title}
            </span>
          )}
          {error && (
            <span className="ml-4 shrink-0 font-mono text-mono-ui text-rose-error" role="alert">
              {error}
            </span>
          )}
        </div>
        <button
          type="button"
          onClick={onGoLive}
          disabled={!canGoLive}
          className="flex shrink-0 items-center gap-2 rounded bg-emerald-live px-4 py-1.5 font-mono text-status-label uppercase text-white transition-all hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-30"
        >
          <Icon name="cast" size={18} />
          Go Live
        </button>
      </div>

      <div className="flex min-h-0 flex-1 items-center justify-center overflow-hidden bg-surface-container-lowest p-6">
        {stage(first)}
      </div>

      {/* Slideshow: the playtime setting lives here, per the spec — adjustable whenever the
          slideshow is the thing selected in the preview panel. */}
      {entry?.type === 'slideshow' && (
        <div className="shrink-0 border-t border-outline-variant bg-surface-container-low px-4 py-3">
          <div className="mb-3 flex items-center gap-3">
            <Icon name="timer" size={18} className="text-on-surface-variant" />
            <label
              htmlFor="slide-seconds"
              className="font-mono text-mono-ui uppercase tracking-widest text-on-surface-variant"
            >
              Seconds per slide
            </label>
            <input
              id="slide-seconds"
              type="range"
              min={MIN_SLIDE_SECONDS}
              max={30}
              step={1}
              value={entry.secondsPerSlide}
              onChange={e => onSetSlideSeconds(entry.id, Number(e.target.value))}
              className="h-1 flex-1 cursor-pointer appearance-none rounded-full bg-surface-container-highest accent-primary"
            />
            <input
              type="number"
              min={MIN_SLIDE_SECONDS}
              max={MAX_SLIDE_SECONDS}
              value={entry.secondsPerSlide}
              onChange={e => {
                const seconds = Number(e.target.value)
                if (Number.isFinite(seconds)) {
                  onSetSlideSeconds(
                    entry.id,
                    Math.min(MAX_SLIDE_SECONDS, Math.max(MIN_SLIDE_SECONDS, Math.round(seconds))),
                  )
                }
              }}
              className="w-16 rounded border border-outline-variant bg-surface-container-lowest px-2 py-1 text-center font-mono text-status-label text-on-surface focus:border-primary focus:outline-none"
            />
          </div>
          <SlideStrip
            ids={ids}
            byId={byId}
            overIndex={overIndex}
            onDragStart={index => (dragIndex.current = index)}
            onDragOverIndex={setOverIndex}
            onDrop={index => {
              if (dragIndex.current !== null && dragIndex.current !== index) {
                onReorderGroup(entry.id, dragIndex.current, index)
              }
              dragIndex.current = null
              setOverIndex(null)
            }}
            onRemove={index => onRemoveFromGroup(entry.id, index)}
          />
        </div>
      )}

      {/* Video queue: order is the whole point, so it is editable right here. */}
      {entry?.type === 'queue' && (
        <div className="shrink-0 border-t border-outline-variant bg-surface-container-low px-4 py-3">
          <p className="mb-2 flex items-center gap-2 font-mono text-mono-ui uppercase tracking-widest text-on-surface-variant">
            <Icon name="reorder" size={16} />
            Play order — drag to rearrange
          </p>
          <SlideStrip
            ids={ids}
            byId={byId}
            numbered
            overIndex={overIndex}
            onDragStart={index => (dragIndex.current = index)}
            onDragOverIndex={setOverIndex}
            onDrop={index => {
              if (dragIndex.current !== null && dragIndex.current !== index) {
                onReorderGroup(entry.id, dragIndex.current, index)
              }
              dragIndex.current = null
              setOverIndex(null)
            }}
            onRemove={index => onRemoveFromGroup(entry.id, index)}
          />
        </div>
      )}
    </section>
  )
}

interface SlideStripProps {
  ids: string[]
  byId: Map<string, MediaLibraryItemDto>
  numbered?: boolean
  overIndex: number | null
  onDragStart: (index: number) => void
  onDragOverIndex: (index: number) => void
  onDrop: (index: number) => void
  onRemove: (index: number) => void
}

/** Horizontal, drag-reorderable strip of a group's members. Shared by slideshow and queue. */
function SlideStrip({
  ids,
  byId,
  numbered,
  overIndex,
  onDragStart,
  onDragOverIndex,
  onDrop,
  onRemove,
}: SlideStripProps) {
  return (
    <ul className="panel-scroll flex gap-2 overflow-x-auto pb-1">
      {ids.map((mediaId, index) => (
        <li
          key={`${mediaId}-${index}`}
          draggable
          onDragStart={() => onDragStart(index)}
          onDragOver={e => {
            e.preventDefault()
            onDragOverIndex(index)
          }}
          onDrop={e => {
            e.preventDefault()
            onDrop(index)
          }}
          className={`group relative shrink-0 cursor-grab overflow-hidden rounded border ${
            overIndex === index ? 'border-primary opacity-60' : 'border-outline-variant'
          }`}
        >
          <MediaThumb item={byId.get(mediaId)} className="h-16 w-24" />
          {numbered && (
            <span className="absolute left-1 top-1 rounded bg-black/70 px-1.5 font-mono text-[10px] text-white">
              {index + 1}
            </span>
          )}
          <button
            type="button"
            onClick={() => onRemove(index)}
            title="Remove from this group"
            className="invisible absolute right-1 top-1 rounded bg-black/70 p-0.5 text-white transition-colors hover:text-rose-error group-hover:visible"
          >
            <Icon name="close" size={12} />
          </button>
        </li>
      ))}
    </ul>
  )
}
