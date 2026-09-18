import { useRef, useState } from 'react'
import Icon from '../../components/Icon'
import type { MediaLibraryItemDto } from '../../lib/types'
import ConfirmDialog from './ConfirmDialog'
import MediaThumb from '../../components/MediaThumb'
import { entryMediaIds, entryTitle, type ScheduleEntry } from './schedule'

interface SchedulePanelProps {
  schedule: ScheduleEntry[]
  byId: Map<string, MediaLibraryItemDto>
  selectedId: string | null
  liveEntryId: string | null
  onPreview: (entryId: string) => void
  onGoLive: (entryId: string) => void
  onRemove: (entryId: string) => void
  onReorder: (fromIndex: number, toIndex: number) => void
}

/**
 * Leftmost column: the service schedule. Items, slideshows and video queues arranged for
 * quick access, in the order the operator wants them. Click semantics match the scripture
 * and songs consoles exactly — single click previews, double click goes live — so muscle
 * memory carries across tabs. Rows drag to reorder; removing one asks first, since losing
 * a built slideshow mid-service is not recoverable by undo.
 */
export default function SchedulePanel({
  schedule,
  byId,
  selectedId,
  liveEntryId,
  onPreview,
  onGoLive,
  onRemove,
  onReorder,
}: SchedulePanelProps) {
  const [confirming, setConfirming] = useState<ScheduleEntry | null>(null)
  const dragIndex = useRef<number | null>(null)
  const [overIndex, setOverIndex] = useState<number | null>(null)

  const icon = (entry: ScheduleEntry) =>
    entry.type === 'slideshow' ? 'slideshow' : entry.type === 'queue' ? 'queue_play_next' : 'image'

  return (
    <aside className="flex w-[300px] shrink-0 flex-col border-r border-outline-variant bg-surface-container-low">
      <div className="flex h-14 shrink-0 items-center justify-between border-b border-outline-variant px-4">
        <div className="flex items-center gap-2 text-on-surface">
          <Icon name="event_note" size={18} />
          <h2 className="font-mono text-status-label uppercase">Schedule</h2>
        </div>
        <span className="font-mono text-mono-ui text-slate-muted">{schedule.length}</span>
      </div>

      <div className="panel-scroll min-h-0 flex-1 overflow-y-auto p-3">
        {schedule.length === 0 ? (
          <p className="mt-8 text-center font-mono text-mono-ui italic text-slate-muted">
            Add media from the gallery to build a schedule.
          </p>
        ) : (
          <ul className="flex flex-col gap-2">
            {schedule.map((entry, index) => {
              const ids = entryMediaIds(entry)
              const isLive = entry.id === liveEntryId
              const isSelected = entry.id === selectedId
              const missing = ids.every(id => !byId.get(id)?.exists)
              return (
                <li
                  key={entry.id}
                  draggable
                  onDragStart={() => (dragIndex.current = index)}
                  onDragOver={e => {
                    e.preventDefault()
                    setOverIndex(index)
                  }}
                  onDragEnd={() => {
                    dragIndex.current = null
                    setOverIndex(null)
                  }}
                  onDrop={e => {
                    e.preventDefault()
                    if (dragIndex.current !== null && dragIndex.current !== index) {
                      onReorder(dragIndex.current, index)
                    }
                    dragIndex.current = null
                    setOverIndex(null)
                  }}
                  className={`group relative ${overIndex === index ? 'opacity-60' : ''}`}
                >
                  <div
                    role="button"
                    tabIndex={0}
                    onClick={() => onPreview(entry.id)}
                    onDoubleClick={() => onGoLive(entry.id)}
                    title="Click to preview · double-click to send live · drag to reorder"
                    className={`flex cursor-pointer select-none items-center gap-2.5 rounded border p-2 text-left transition-colors ${
                      isLive
                        ? 'live-glow border-emerald-live/50 border-l-4 border-l-emerald-live bg-surface-container-high'
                        : isSelected
                          ? 'border-primary bg-primary/5'
                          : 'border-outline-variant bg-surface-container hover:border-primary/30'
                    }`}
                  >
                    <MediaThumb
                      item={byId.get(ids[0])}
                      className="h-11 w-14 shrink-0 rounded"
                    />
                    <div className="min-w-0 flex-1">
                      <span className="flex items-center gap-1.5">
                        {isLive && (
                          <span className="h-2 w-2 shrink-0 animate-pulse rounded-full bg-emerald-live" />
                        )}
                        <Icon
                          name={icon(entry)}
                          size={14}
                          className="shrink-0 text-on-surface-variant"
                        />
                        <span
                          className={`truncate text-body-md font-semibold ${
                            missing ? 'text-rose-error' : 'text-on-surface'
                          }`}
                        >
                          {entryTitle(entry, byId)}
                        </span>
                      </span>
                      <span className="mt-0.5 block font-mono text-mono-ui text-slate-muted">
                        {entry.type === 'slideshow'
                          ? `${ids.length} slides · ${entry.secondsPerSlide}s`
                          : entry.type === 'queue'
                            ? `${ids.length} videos`
                            : (byId.get(entry.mediaId)?.kind ?? 'missing')}
                      </span>
                    </div>
                  </div>
                  <button
                    type="button"
                    onClick={() => setConfirming(entry)}
                    title="Remove from schedule"
                    className="invisible absolute right-1.5 top-1.5 flex items-center rounded bg-surface-container-highest p-0.5 text-on-surface-variant transition-colors hover:text-rose-error group-hover:visible"
                  >
                    <Icon name="close" size={14} />
                  </button>
                </li>
              )
            })}
          </ul>
        )}
      </div>

      {confirming && (
        <ConfirmDialog
          title="Remove from schedule?"
          message={
            confirming.type === 'item'
              ? `“${entryTitle(confirming, byId)}” will be removed from the schedule. The file stays in your gallery.`
              : `“${entryTitle(confirming, byId)}” will be removed from the schedule. ${
                  confirming.type === 'slideshow' ? 'Slideshows' : 'Queues'
                } only exist here, so this one is gone for good — the files stay in your gallery.`
          }
          confirmLabel="Remove"
          onConfirm={() => onRemove(confirming.id)}
          onClose={() => setConfirming(null)}
        />
      )}
    </aside>
  )
}
