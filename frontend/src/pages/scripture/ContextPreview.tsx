import { useEffect, useRef } from 'react'
import Icon from '../../components/Icon'
import type { ChapterDto } from '../../lib/types'

interface ContextPreviewProps {
  chapter: ChapterDto | null
  selectedVerses: number[]
  /** Verses of the current chapter that are live on the displays right now. */
  liveVerses: number[]
  loading: boolean
  error: string
  onNavigateChapter: (direction: -1 | 1) => void
  onToggleVerse: (verse: number, extendRange: boolean) => void
  onReplaceLive: (verse: number) => void
  onAddToLive: (verse: number) => void
  onGoLive: () => void
}

export default function ContextPreview({
  chapter,
  selectedVerses,
  liveVerses,
  loading,
  error,
  onNavigateChapter,
  onToggleVerse,
  onReplaceLive,
  onAddToLive,
  onGoLive,
}: ContextPreviewProps) {
  const selected = new Set(selectedVerses)
  const live = new Set(liveVerses)
  const scrollRef = useRef<HTMLDivElement>(null)
  // Scroll target: prefer whatever is live, else the current selection.
  const anchorVerse =
    liveVerses.length > 0
      ? Math.min(...liveVerses)
      : selectedVerses.length > 0
        ? Math.min(...selectedVerses)
        : null

  // Keep the live/selected verse in view when a detection or search jumps the preview.
  useEffect(() => {
    if (anchorVerse === null) return
    scrollRef.current
      ?.querySelector(`[data-verse="${anchorVerse}"]`)
      ?.scrollIntoView({ block: 'center', behavior: 'smooth' })
  }, [chapter?.book, chapter?.chapter, anchorVerse])

  return (
    <section className="flex min-w-0 flex-1 flex-col bg-surface">
      <div className="flex h-14 shrink-0 items-center justify-between border-b border-outline-variant px-4">
        <div className="flex items-center gap-2 text-on-surface">
          <Icon name="preview" size={18} />
          <h2 className="font-mono text-status-label uppercase">Context Preview</h2>
          {error && (
            <span className="ml-4 font-mono text-mono-ui text-rose-error" role="alert">
              {error}
            </span>
          )}
        </div>
        <div className="flex items-center gap-3">
          <div className="flex items-center rounded border border-outline-variant bg-surface-container">
            <button
              type="button"
              onClick={() => onNavigateChapter(-1)}
              disabled={!chapter}
              title="Previous chapter"
              className="flex items-center px-2 py-1.5 text-on-surface-variant transition-colors hover:text-on-surface disabled:opacity-30"
            >
              <Icon name="chevron_left" />
            </button>
            <span className="min-w-32 border-x border-outline-variant px-3 py-1.5 text-center font-mono text-status-label">
              {chapter ? `${chapter.book} ${chapter.chapter}` : '—'}
            </span>
            <button
              type="button"
              onClick={() => onNavigateChapter(1)}
              disabled={!chapter}
              title="Next chapter"
              className="flex items-center px-2 py-1.5 text-on-surface-variant transition-colors hover:text-on-surface disabled:opacity-30"
            >
              <Icon name="chevron_right" />
            </button>
          </div>
          <button
            type="button"
            onClick={onGoLive}
            disabled={selectedVerses.length === 0}
            className="flex items-center gap-2 rounded bg-emerald-live px-4 py-1.5 font-mono text-status-label uppercase text-white transition-all hover:brightness-110 disabled:cursor-not-allowed disabled:opacity-30"
          >
            <Icon name="cast" size={18} />
            Go Live
          </button>
        </div>
      </div>

      <div ref={scrollRef} className="panel-scroll min-h-0 flex-1 overflow-y-auto px-8 py-6">
        {!chapter ? (
          <div className="flex h-full items-center justify-center">
            <p className="font-mono text-mono-ui italic text-slate-muted">
              {loading ? 'Loading chapter…' : 'Search a reference or start voice detection.'}
            </p>
          </div>
        ) : (
          <ol className="mx-auto flex max-w-3xl flex-col gap-3">
            {chapter.verses.map(verse => {
              const isSelected = selected.has(verse.number)
              const isLive = live.has(verse.number)
              const isActive = isSelected || isLive
              // Live wins the container treatment (emerald + glow); a merely-selected
              // verse gets the primary/blue border so the operator can always tell
              // what is actually on the displays versus what is staged.
              const container = isLive
                ? 'live-glow border-emerald-live bg-emerald-live/10'
                : isSelected
                  ? 'border-primary bg-primary/5'
                  : 'border-transparent opacity-40 transition-opacity hover:opacity-100'
              return (
                <li
                  key={verse.number}
                  data-verse={verse.number}
                  data-live={isLive || undefined}
                  onClick={e => onToggleVerse(verse.number, e.shiftKey)}
                  onDoubleClick={() => onReplaceLive(verse.number)}
                  className={`group flex cursor-pointer select-none gap-4 rounded-lg border-2 px-5 py-4 ${container}`}
                  title="Click to select · double-click to send live · shift-click to extend"
                >
                  <span
                    className={`font-mono text-status-label ${
                      isLive ? 'text-emerald-live' : isSelected ? 'text-primary' : 'text-slate-muted'
                    }`}
                  >
                    {verse.number}
                  </span>
                  <div className="min-w-0 flex-1">
                    <div className="flex items-start justify-between gap-4">
                      <p
                        className={
                          isActive
                            ? 'font-display text-headline-md text-on-surface'
                            : 'text-body-lg text-on-surface-variant'
                        }
                      >
                        {verse.text}
                      </p>
                      {isLive && (
                        <span className="mt-1 flex shrink-0 items-center gap-1.5 rounded-full border border-emerald-live/40 bg-emerald-live/10 px-2 py-0.5 font-mono text-[10px] uppercase tracking-widest text-emerald-live">
                          <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-emerald-live" />
                          Live
                        </span>
                      )}
                    </div>
                    <button
                      type="button"
                      onClick={e => {
                        e.stopPropagation()
                        onAddToLive(verse.number)
                      }}
                      title="Add verse to live item"
                      className={`mt-2 flex items-center rounded border border-outline-variant bg-surface-container px-2.5 py-1 text-on-surface-variant transition-all hover:border-primary hover:text-primary ${
                        isActive ? '' : 'invisible group-hover:visible'
                      }`}
                    >
                      <Icon name="playlist_add" size={18} />
                    </button>
                  </div>
                </li>
              )
            })}
          </ol>
        )}
      </div>
    </section>
  )
}
