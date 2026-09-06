import Icon from '../../components/Icon'
import VerseCanvas from '../../components/VerseCanvas'
import type {
  ContentType,
  DisplayConfig,
  FontDto,
  LiveItemDto,
  MediaDisplayConfig,
  ViewportRect,
} from '../../lib/types'
import { viewportOf } from './stageConfig'
import ViewportOverlay from './ViewportOverlay'

interface PreviewPaneProps {
  draft: DisplayConfig
  fonts: FontDto[]
  liveItem: LiveItemDto | null
  /** Which content type's config is being edited — the preview and its draggable frame follow it. */
  activeType: ContentType
  /** The viewport is not editable while the display mirrors another display. */
  editable: boolean
  onViewportChange: (rect: ViewportRect) => void
}

// Shown when nothing is live so the operator always styles against real text.
const PLACEHOLDER: LiveItemDto = {
  id: 'preview-placeholder',
  reference: 'John 3:16',
  text: 'For God so loved the world, that he gave his only begotten Son, that whosoever believeth in him should not perish, but have everlasting life.',
  translation: 'KJV',
  source: 'preview',
  at: '',
  kind: 'scripture',
  book: 'John',
  chapter: 3,
  verseStart: 16,
  verseEnd: 16,
  mediaId: null,
  mediaKind: null,
  mediaLoop: true,
}

/** No live media channel yet: show the window's placement, fill, and fit mode. */
function MediaWindowPreview({ media }: { media: MediaDisplayConfig }) {
  return (
    <div
      className="absolute flex items-center justify-center"
      style={{
        left: `${media.viewport.x}%`,
        top: `${media.viewport.y}%`,
        width: `${media.viewport.width}%`,
        height: `${media.viewport.height}%`,
        backgroundColor: media.backgroundColor,
      }}
    >
      <span className="flex items-center gap-2 font-mono text-mono-ui uppercase tracking-widest text-slate-muted">
        <Icon name="image" size={18} />
        Media · {media.fit === 'cover' ? 'fill' : 'fit'}
      </span>
    </div>
  )
}

export default function PreviewPane({
  draft,
  fonts,
  liveItem,
  activeType,
  editable,
  onViewportChange,
}: PreviewPaneProps) {
  return (
    <section className="flex min-h-0 flex-1 flex-col gap-4 overflow-hidden p-8">
      <div className="flex shrink-0 items-center justify-between">
        <span className="flex items-center gap-2 rounded-full border border-surface-variant bg-surface-container-lowest px-4 py-1.5 font-mono text-mono-ui uppercase tracking-widest text-on-surface-variant">
          <span
            className={
              liveItem
                ? 'h-2 w-2 rounded-full bg-emerald-live'
                : 'h-2 w-2 rounded-full bg-slate-muted'
            }
          />
          {liveItem ? 'Live preview context' : 'Preview · sample verse'}
        </span>
        <span className="rounded-full border border-surface-variant bg-surface-container-lowest px-4 py-1.5 font-mono text-mono-ui text-on-surface-variant">
          1920 × 1080 (16:9)
        </span>
      </div>
      {/* containerType lets the 16:9 box size against whichever axis is tighter. */}
      <div
        className="flex min-h-0 min-w-0 flex-1 items-center justify-center"
        style={{ containerType: 'size' }}
      >
        <div
          className="relative aspect-video rounded-lg border border-surface-variant shadow-2xl"
          style={{ width: 'min(100%, calc(100cqh * 16 / 9))' }}
        >
          {/* Checkerboard = transparent on the real display (alpha in OBS/compositing). */}
          <div
            className="absolute inset-0 overflow-hidden rounded-lg"
            style={{
              background:
                'repeating-conic-gradient(#131b2e 0% 25%, #0b1326 0% 50%) 0 0 / 24px 24px',
            }}
          >
            {activeType === 'media' ? (
              <MediaWindowPreview media={draft.media} />
            ) : (
              <VerseCanvas
                text={draft[activeType].text}
                reference={activeType === 'scripture' ? draft.scripture.reference : null}
                fonts={fonts}
                item={liveItem ?? PLACEHOLDER}
              />
            )}
          </div>
          {editable && (
            <ViewportOverlay rect={viewportOf(draft, activeType)} onChange={onViewportChange} />
          )}
        </div>
      </div>
    </section>
  )
}
