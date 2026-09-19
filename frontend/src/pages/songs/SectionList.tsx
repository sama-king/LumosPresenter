import { useEffect, useLayoutEffect, useRef } from 'react'
import Icon from '../../components/Icon'
import type { SongDto } from '../../lib/types'
import { sectionLabel } from './songQueue'

interface SectionListProps {
  song: SongDto | null
  loading: boolean
  /** sectionPosition of the section currently live on the displays, if from this song. */
  liveSectionPosition: number | null
  /** sectionPosition staged via single-click select (blue), not yet live. */
  selectedSectionPosition: number | null
  onSelectSection: (position: number) => void
  onGoLiveSection: (position: number) => void
  onEdit: () => void
}

/**
 * Middle column: the selected song's sections as slides. Single-click selects (blue,
 * staged), double-click sends the section to the displays (emerald, live) — mirrors the
 * scripture ContextPreview interaction model.
 */
export default function SectionList({
  song,
  loading,
  liveSectionPosition,
  selectedSectionPosition,
  onSelectSection,
  onGoLiveSection,
  onEdit,
}: SectionListProps) {
  const scrollRef = useRef<HTMLDivElement>(null)

  // A newly previewed or pushed song starts at its first section. Keyed on the object, not
  // the id, so re-picking the song already shown also brings it back to the top.
  useLayoutEffect(() => {
    if (scrollRef.current) scrollRef.current.scrollTop = 0
  }, [song])

  // Keep the live section in view as the arrow keys walk through the song.
  useEffect(() => {
    if (liveSectionPosition === null) return
    scrollRef.current
      ?.querySelector(`[data-section="${liveSectionPosition}"]`)
      ?.scrollIntoView({ block: 'nearest', behavior: 'smooth' })
  }, [song, liveSectionPosition])

  return (
    <section className="flex min-w-0 flex-1 flex-col bg-surface">
      <div className="flex h-14 shrink-0 items-center justify-between border-b border-outline-variant px-4">
        <div className="flex min-w-0 items-center gap-2 text-on-surface">
          <Icon name="queue_music" size={18} />
          <h2 className="truncate font-mono text-status-label uppercase">
            {song ? song.title : 'Sections'}
          </h2>
          {song?.author && (
            <span className="truncate font-mono text-mono-ui text-slate-muted">· {song.author}</span>
          )}
        </div>
        {song && (
          <button
            type="button"
            onClick={onEdit}
            className="flex items-center gap-2 rounded border border-outline-variant px-3 py-1.5 font-mono text-status-label uppercase text-on-surface-variant transition-colors hover:border-primary hover:text-primary"
          >
            <Icon name="edit" size={18} />
            Edit
          </button>
        )}
      </div>

      <div ref={scrollRef} className="panel-scroll min-h-0 flex-1 overflow-y-auto px-8 py-6">
        {!song ? (
          <div className="flex h-full items-center justify-center">
            <p className="font-mono text-mono-ui italic text-slate-muted">
              {loading ? 'Loading song…' : 'Select a song from the library.'}
            </p>
          </div>
        ) : (
          <ol className="mx-auto flex max-w-3xl flex-col gap-3">
            {song.sections.map(section => {
              const isLive = section.position === liveSectionPosition
              const isSelected = section.position === selectedSectionPosition
              // Live wins the emerald glow; a merely-selected section gets the blue
              // border so the operator can tell what is staged versus what is on-screen.
              const container = isLive
                ? 'live-glow border-emerald-live bg-emerald-live/10'
                : isSelected
                  ? 'border-primary bg-primary/5'
                  : 'border-outline-variant transition-colors hover:border-primary/50'
              return (
                <li
                  key={section.position}
                  data-section={section.position}
                  data-live={isLive || undefined}
                  onClick={() => onSelectSection(section.position)}
                  onDoubleClick={() => onGoLiveSection(section.position)}
                  className={`group flex cursor-pointer select-none gap-4 rounded-lg border-2 px-5 py-4 ${container}`}
                  title="Click to select · double-click to send live"
                >
                  <div className="min-w-0 flex-1">
                    <div className="mb-2 flex items-center justify-between gap-4">
                      <span
                        className={`font-mono text-status-label uppercase ${
                          isLive ? 'text-emerald-live' : isSelected ? 'text-primary' : 'text-slate-muted'
                        }`}
                      >
                        {sectionLabel(section)}
                      </span>
                      {isLive && (
                        <span className="flex shrink-0 items-center gap-1.5 rounded-full border border-emerald-live/40 bg-emerald-live/10 px-2 py-0.5 font-mono text-[10px] uppercase tracking-widest text-emerald-live">
                          <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-emerald-live" />
                          Live
                        </span>
                      )}
                    </div>
                    <p
                      className={`whitespace-pre-line ${
                        isLive || isSelected
                          ? 'font-display text-headline-md text-on-surface'
                          : 'text-body-lg text-on-surface-variant'
                      }`}
                    >
                      {section.text}
                    </p>
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
