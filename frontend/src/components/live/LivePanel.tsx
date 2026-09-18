import Icon from '../Icon'
import { useLive, type LiveSlide } from '../../lib/live'
import LiveVideoMonitor from './LiveVideoMonitor'
import MediaThumb from '../MediaThumb'

/**
 * The live panel: one instance of it, shared by scripture, songs and media.
 *
 * Only one thing can be on the displays at a time, so there is only one queue — whichever tab
 * filled it. That is the whole point of hoisting this out of the three pages that used to
 * carry a copy each: the operator can send a video live from the media tab, walk over to
 * scripture, and still see (and pause) what is actually on screen. The panel renders whatever
 * kind of slide the queue holds, and video gets a transport strip because playback is driven
 * from here — the displays only follow.
 */
export default function LivePanel() {
  const live = useLive()
  const { slides, liveId, liveSlide, advance, autoPlaying, title, backdrop } = live
  const multiple = slides.length > 1
  // Only words can be cleared on their own; media has no text window to leave behind.
  const textLive = liveSlide !== null && liveSlide.kind !== 'media'
  const anythingUp = slides.length > 0 || live.item !== null || backdrop !== null

  return (
    <aside className="flex w-[300px] shrink-0 flex-col border-l border-outline-variant bg-surface-container-low">
      <div className="flex h-14 shrink-0 items-center justify-between border-b border-outline-variant px-4">
        <div className="flex min-w-0 items-center gap-2 text-emerald-live">
          <Icon name="cast" size={18} />
          <h2 className="font-mono text-status-label uppercase">Live</h2>
        </div>
        <div className="flex shrink-0 items-center gap-3">
          <button
            type="button"
            onClick={live.clearText}
            disabled={!textLive}
            title="Remove the words; keep the background up"
            className="font-mono text-mono-ui uppercase tracking-widest text-on-surface-variant transition-colors hover:text-on-surface disabled:opacity-30"
          >
            Clear text
          </button>
          <button
            type="button"
            onClick={live.clearAll}
            disabled={!anythingUp}
            title="Remove everything from the displays"
            className="font-mono text-mono-ui uppercase tracking-widest text-rose-error transition-opacity hover:opacity-80 disabled:opacity-30"
          >
            Clear all
          </button>
        </div>
      </div>

      {backdrop !== null && live.item === null && (
        <p className="flex shrink-0 items-center gap-1.5 border-b border-outline-variant px-4 py-1.5 font-mono text-mono-ui text-slate-muted">
          <Icon name="wallpaper" size={14} />
          {backdrop === 'songs' ? 'Songs' : 'Scripture'} background showing
        </p>
      )}

      {live.error && (
        <p className="shrink-0 bg-rose-error/10 px-4 py-1.5 font-mono text-mono-ui text-rose-error" role="alert">
          {live.error}
        </p>
      )}

      {slides.length === 0 ? (
        <p className="mt-8 px-4 text-center font-mono text-mono-ui italic text-slate-muted">
          Nothing is live. Send a verse, a song section or a media item to the displays.
        </p>
      ) : (
        <>
          {liveSlide?.kind === 'media' && (
            <div className="shrink-0 border-b border-outline-variant p-3">
              {liveSlide.mediaKind === 'video' ? (
                <LiveVideoMonitor slide={liveSlide} />
              ) : (
                <div className="live-glow overflow-hidden rounded border border-emerald-live/50">
                  <MediaThumb
                    item={{ id: liveSlide.mediaId, kind: 'image' }}
                    className="h-32 w-full"
                  />
                </div>
              )}
              <p className="mt-2 flex items-center gap-1.5">
                <span className="h-2 w-2 shrink-0 animate-pulse rounded-full bg-emerald-live" />
                <span className="truncate text-body-md font-semibold text-on-surface">
                  {title || liveSlide.title}
                </span>
              </p>
              {multiple && (
                <p className="mt-0.5 font-mono text-mono-ui text-slate-muted">
                  {slides.findIndex(slide => slide.id === liveId) + 1} of {slides.length}
                  {advance.mode === 'timer' && ` · ${advance.secondsPerSlide}s per slide`}
                </p>
              )}
            </div>
          )}

          {multiple && (
            <div className="flex shrink-0 items-center justify-center gap-2 border-b border-outline-variant py-2.5">
              <button
                type="button"
                onClick={() => {
                  live.setAutoPlaying(false)
                  live.step(-1)
                }}
                title="Previous"
                className="rounded border border-outline-variant bg-surface-container p-1.5 text-on-surface-variant transition-colors hover:text-on-surface"
              >
                <Icon name="skip_previous" size={18} />
              </button>
              {advance.mode === 'timer' && (
                <button
                  type="button"
                  onClick={() => live.setAutoPlaying(!autoPlaying)}
                  title={autoPlaying ? 'Pause slideshow' : 'Play slideshow'}
                  className="rounded border border-emerald-live/50 bg-emerald-live/10 p-1.5 text-emerald-live transition-colors hover:bg-emerald-live/20"
                >
                  <Icon name={autoPlaying ? 'pause' : 'play_arrow'} size={18} />
                </button>
              )}
              <button
                type="button"
                onClick={() => {
                  live.setAutoPlaying(false)
                  live.step(1)
                }}
                title="Next"
                className="rounded border border-outline-variant bg-surface-container p-1.5 text-on-surface-variant transition-colors hover:text-on-surface"
              >
                <Icon name="skip_next" size={18} />
              </button>
            </div>
          )}

          <div className="panel-scroll min-h-0 flex-1 overflow-y-auto p-3">
            <ul className="flex flex-col gap-2">
              {slides.map((slide, position) => (
                <li key={slide.id} className="group relative">
                  <button
                    type="button"
                    onClick={() => {
                      live.setAutoPlaying(false)
                      live.show(slide.id)
                    }}
                    title={slide.id === liveId ? 'Live on displays' : 'Show on displays'}
                    className={
                      slide.id === liveId
                        ? 'live-glow w-full rounded border border-emerald-live/50 border-l-4 border-l-emerald-live bg-surface-container-high p-2.5 text-left'
                        : 'w-full rounded border border-outline-variant bg-surface-container p-2.5 text-left transition-colors hover:border-emerald-live/40'
                    }
                  >
                    <SlideRow slide={slide} position={position} isLive={slide.id === liveId} />
                  </button>
                  {live.origin === 'scripture' && (
                    <button
                      type="button"
                      onClick={() => live.removeSlide(slide.id)}
                      title="Remove from queue"
                      className="invisible absolute right-2 top-2 flex items-center rounded bg-surface-container-highest p-0.5 text-on-surface-variant transition-colors hover:text-rose-error group-hover:visible"
                    >
                      <Icon name="close" size={14} />
                    </button>
                  )}
                </li>
              ))}
            </ul>
          </div>
        </>
      )}
    </aside>
  )
}

/** One queue row. The three slide kinds read differently enough to be worth drawing apart. */
function SlideRow({
  slide,
  position,
  isLive,
}: {
  slide: LiveSlide
  position: number
  isLive: boolean
}) {
  if (slide.kind === 'media') {
    return (
      <span className="flex items-center gap-2.5">
        <span
          className={`w-5 shrink-0 text-center font-mono text-mono-ui ${
            isLive ? 'text-emerald-live' : 'text-slate-muted'
          }`}
        >
          {position + 1}
        </span>
        <MediaThumb
          item={{ id: slide.mediaId, kind: slide.mediaKind }}
          className="h-10 w-14 shrink-0 rounded"
        />
        <span className="truncate font-mono text-mono-ui text-on-surface">{slide.title}</span>
      </span>
    )
  }

  return (
    <>
      <span className="flex items-center gap-2">
        {isLive && <span className="h-2 w-2 shrink-0 animate-pulse rounded-full bg-emerald-live" />}
        <span
          className={
            slide.kind === 'song'
              ? 'font-mono text-status-label uppercase text-on-surface'
              : 'text-body-md font-semibold text-on-surface'
          }
        >
          {slide.kind === 'song'
            ? (slide.label ?? `Section ${slide.sectionPosition + 1}`)
            : slide.reference}
        </span>
      </span>
      <span className="mt-1 line-clamp-2 block whitespace-pre-line text-mono-ui italic text-on-surface-variant">
        {slide.kind === 'song' ? slide.text : `“${slide.text}”`}
      </span>
    </>
  )
}
