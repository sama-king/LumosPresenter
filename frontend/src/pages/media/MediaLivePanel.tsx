import Icon from '../../components/Icon'
import type { MediaLibraryItemDto } from '../../lib/types'
import MediaThumb from './MediaThumb'
import { entryTitle, type ScheduleEntry } from './schedule'

interface MediaLivePanelProps {
  /** Schedule entry currently on the displays, or null when nothing is live. */
  entry: ScheduleEntry | null
  /** Ids the live entry plays, in order. */
  ids: string[]
  /** Index within `ids` that is on the displays right now. */
  index: number
  byId: Map<string, MediaLibraryItemDto>
  playing: boolean
  onSelectIndex: (index: number) => void
  onTogglePlay: () => void
  onStep: (delta: -1 | 1) => void
  onClear: () => void
}

/**
 * Right column: what the displays are showing. For a single item that is one tile; for a
 * slideshow or queue it is the whole run with the current position marked, plus transport
 * controls. Advance is driven from here (the console owns the timer and pushes each frame),
 * so the operator can always see and override what comes next — a display-side timer would
 * put the truth somewhere nobody is looking.
 */
export default function MediaLivePanel({
  entry,
  ids,
  index,
  byId,
  playing,
  onSelectIndex,
  onTogglePlay,
  onStep,
  onClear,
}: MediaLivePanelProps) {
  const isGroup = entry !== null && entry.type !== 'item'

  return (
    <aside className="flex w-[300px] shrink-0 flex-col border-l border-outline-variant bg-surface-container-low">
      <div className="flex h-14 shrink-0 items-center justify-between border-b border-outline-variant px-4">
        <div className="flex items-center gap-2 text-emerald-live">
          <Icon name="cast" size={18} />
          <h2 className="font-mono text-status-label uppercase">Live</h2>
        </div>
        <button
          type="button"
          onClick={onClear}
          disabled={entry === null}
          className="font-mono text-mono-ui uppercase tracking-widest text-rose-error transition-opacity hover:opacity-80 disabled:opacity-30"
        >
          Clear
        </button>
      </div>

      {entry === null ? (
        <p className="mt-8 px-4 text-center font-mono text-mono-ui italic text-slate-muted">
          Double-click a schedule item or gallery tile to send it live.
        </p>
      ) : (
        <>
          <div className="shrink-0 border-b border-outline-variant p-3">
            <div className="live-glow overflow-hidden rounded border border-emerald-live/50">
              <MediaThumb item={byId.get(ids[index])} className="h-32 w-full" />
            </div>
            <p className="mt-2 flex items-center gap-1.5">
              <span className="h-2 w-2 shrink-0 animate-pulse rounded-full bg-emerald-live" />
              <span className="truncate text-body-md font-semibold text-on-surface">
                {entryTitle(entry, byId)}
              </span>
            </p>
            {isGroup && (
              <p className="mt-0.5 font-mono text-mono-ui text-slate-muted">
                {index + 1} of {ids.length}
                {entry.type === 'slideshow' && ` · ${entry.secondsPerSlide}s per slide`}
              </p>
            )}
          </div>

          {isGroup && (
            <div className="flex shrink-0 items-center justify-center gap-2 border-b border-outline-variant py-2.5">
              <button
                type="button"
                onClick={() => onStep(-1)}
                title="Previous"
                className="rounded border border-outline-variant bg-surface-container p-1.5 text-on-surface-variant transition-colors hover:text-on-surface"
              >
                <Icon name="skip_previous" size={18} />
              </button>
              {entry.type === 'slideshow' && (
                <button
                  type="button"
                  onClick={onTogglePlay}
                  title={playing ? 'Pause slideshow' : 'Play slideshow'}
                  className="rounded border border-emerald-live/50 bg-emerald-live/10 p-1.5 text-emerald-live transition-colors hover:bg-emerald-live/20"
                >
                  <Icon name={playing ? 'pause' : 'play_arrow'} size={18} />
                </button>
              )}
              <button
                type="button"
                onClick={() => onStep(1)}
                title="Next"
                className="rounded border border-outline-variant bg-surface-container p-1.5 text-on-surface-variant transition-colors hover:text-on-surface"
              >
                <Icon name="skip_next" size={18} />
              </button>
            </div>
          )}

          <div className="panel-scroll min-h-0 flex-1 overflow-y-auto p-3">
            {isGroup && (
              <ul className="flex flex-col gap-2">
                {ids.map((mediaId, position) => {
                  const isCurrent = position === index
                  return (
                    <li key={`${mediaId}-${position}`}>
                      <button
                        type="button"
                        onClick={() => onSelectIndex(position)}
                        title="Send this one to the displays"
                        className={`flex w-full items-center gap-2.5 rounded border p-1.5 text-left transition-colors ${
                          isCurrent
                            ? 'border-emerald-live/50 bg-surface-container-high'
                            : 'border-outline-variant bg-surface-container hover:border-emerald-live/40'
                        }`}
                      >
                        <span
                          className={`w-5 shrink-0 text-center font-mono text-mono-ui ${
                            isCurrent ? 'text-emerald-live' : 'text-slate-muted'
                          }`}
                        >
                          {position + 1}
                        </span>
                        <MediaThumb
                          item={byId.get(mediaId)}
                          className="h-10 w-14 shrink-0 rounded"
                        />
                        <span className="truncate text-mono-ui font-mono text-on-surface">
                          {byId.get(mediaId)?.title ?? 'Missing file'}
                        </span>
                      </button>
                    </li>
                  )
                })}
              </ul>
            )}
          </div>
        </>
      )}
    </aside>
  )
}
