import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { cssFamilyFor } from '../lib/fonts'
import { mediaFileUrl } from '../lib/media'
import type {
  BackgroundConfig,
  FontDto,
  LiveItemDto,
  ReferenceConfig,
  TextDisplayConfig,
} from '../lib/types'

interface VerseCanvasProps {
  text: TextDisplayConfig
  /** Reference line config; null/absent hides it (songs have no reference line). */
  reference?: ReferenceConfig | null
  fonts: FontDto[]
  item: LiveItemDto | null
  /** Paint the window background even with no item — the state after a text-only clear. */
  showBackground?: boolean
  className?: string
}

const JUSTIFY: Record<TextDisplayConfig['verticalAlign'], string> = {
  top: 'flex-start',
  middle: 'center',
  bottom: 'flex-end',
}

const ALIGN: Record<TextDisplayConfig['horizontalAlign'], string> = {
  left: 'flex-start',
  center: 'center',
  right: 'flex-end',
}

/** Auto-fit floor: below this the text is unreadable on a projector anyway. */
const MIN_FONT_PX = 12

/**
 * Song sections are written as separate lines and the operator lays them out that way in
 * the editor and the live queue, so the projection has to honour those breaks too — HTML
 * would otherwise collapse them into one run-on paragraph. `pre-line` keeps the newlines
 * while still collapsing incidental double spaces and letting long lines wrap, which is
 * what lyrics want; scripture has no newlines, so it renders identically to before.
 *
 * Shared with the offscreen measurer deliberately: the auto-fit search sizes text by
 * measuring it, so if the two disagreed about line breaks the chosen size would be wrong.
 */
const VERSE_WHITE_SPACE = 'pre-line' as const

/**
 * The single source of rendering truth for a text window: the projection surface
 * (DisplayPage) and the stage-config preview both render through it, so the preview
 * is WYSIWYG. All pixel values in the config are relative to a 1920-wide frame and
 * scaled by the actual container width.
 *
 * Only the text viewport is painted (with the configured background); everything
 * outside stays transparent, so the display reads as alpha in OBS/compositing contexts.
 * With nothing live the whole canvas is transparent too — unless showBackground is set
 * (the operator cleared only the text), in which case the empty window keeps its
 * background, and a looping video carries on without restarting.
 *
 * Text auto-fits: text.fontSizePx is the MAXIMUM size, and a binary search against
 * an invisible measurer shrinks long passages until verse + reference fit the
 * viewport's inner box (viewport minus padding).
 */
export default function VerseCanvas({
  text,
  reference = null,
  fonts,
  item,
  showBackground = false,
  className = '',
}: VerseCanvasProps) {
  const rootRef = useRef<HTMLDivElement>(null)
  const measureRef = useRef<HTMLDivElement>(null)
  const [size, setSize] = useState({ width: 0, height: 0 })
  const [fitSize, setFitSize] = useState<number | null>(null)
  const [fontsReadyTick, setFontsReadyTick] = useState(0)

  const scale = size.width / 1920

  useEffect(() => {
    const root = rootRef.current
    if (!root) return
    const observer = new ResizeObserver(entries => {
      const rect = entries[0].contentRect
      setSize({ width: rect.width, height: rect.height })
    })
    observer.observe(root)
    return () => observer.disconnect()
  }, [])

  // Fonts load async; re-measure once the faces are actually available so the
  // fit isn't computed against fallback-font metrics.
  useEffect(() => {
    let active = true
    void document.fonts?.ready.then(() => {
      if (active) setFontsReadyTick(t => t + 1)
    })
    return () => {
      active = false
    }
  }, [text.fontSlug, reference?.fontSlug])

  const { viewport, padding } = text
  const verseFamily = cssFamilyFor(fonts, text.fontSlug)
  const referenceLine = item ? `${item.reference} · ${item.translation}` : null
  // Position is "{above|below}-{left|center|right}": the first part flows the reference above
  // or below the verse; the second aligns it horizontally within the text column.
  const referenceAbove = reference?.position.startsWith('above') ?? false
  const referenceSide = reference?.position.endsWith('left')
    ? 'flex-start'
    : reference?.position.endsWith('right')
      ? 'flex-end'
      : 'center'

  // Binary-search the largest font size (≤ configured max) whose wrapped verse —
  // plus the in-flow reference line — fits the viewport's inner box.
  useLayoutEffect(() => {
    const measure = measureRef.current
    if (!measure || !item || scale <= 0) return

    const availWidth =
      (viewport.width / 100) * size.width - (padding.left + padding.right) * scale
    const availHeight =
      (viewport.height / 100) * size.height - (padding.top + padding.bottom) * scale
    const maxPx = text.fontSizePx * scale
    if (availWidth <= 0 || availHeight <= 0) {
      setFitSize(MIN_FONT_PX * scale)
      return
    }

    measure.style.width = `${availWidth}px`

    let reserved = 0
    if (reference?.show && referenceLine) {
      Object.assign(measure.style, {
        fontFamily: cssFamilyFor(fonts, reference.fontSlug),
        fontWeight: String(reference.fontWeight),
        fontSize: `${reference.fontSizePx * scale}px`,
        letterSpacing: '0.05em',
        textTransform: 'uppercase',
        lineHeight: 'normal',
        whiteSpace: 'normal',
      })
      measure.textContent = referenceLine
      reserved = measure.offsetHeight + 32 * scale
    }

    Object.assign(measure.style, {
      fontFamily: verseFamily,
      fontWeight: String(text.fontWeight),
      letterSpacing: '-0.02em',
      textTransform: 'none',
      lineHeight: '1.2',
      // Must match the rendered paragraph exactly: song sections carry their own line
      // breaks, and measuring them collapsed would size the text for fewer lines than are
      // actually drawn, overflowing the viewport.
      whiteSpace: VERSE_WHITE_SPACE,
    })
    // Measures exactly what is drawn — no decoration around the text, so the fitted size
    // matches the rendered paragraph.
    measure.textContent = item.text

    const fits = (px: number) => {
      measure.style.fontSize = `${px}px`
      return measure.offsetHeight + reserved <= availHeight
    }

    if (fits(maxPx)) {
      setFitSize(maxPx)
      return
    }
    let low = Math.min(MIN_FONT_PX * scale, maxPx)
    let high = maxPx
    for (let i = 0; i < 10; i++) {
      const mid = (low + high) / 2
      if (fits(mid)) low = mid
      else high = mid
    }
    setFitSize(low)
  }, [
    item,
    text,
    fonts,
    scale,
    size,
    viewport,
    padding,
    reference,
    verseFamily,
    referenceLine,
    fontsReadyTick,
  ])

  const referenceStyle: React.CSSProperties | null = reference && {
    position: 'relative', // above the media background layer
    zIndex: 1,
    fontFamily: cssFamilyFor(fonts, reference.fontSlug),
    fontWeight: reference.fontWeight,
    fontSize: reference.fontSizePx * scale,
    color: reference.color,
    letterSpacing: '0.05em',
    textTransform: 'uppercase' as const,
    // In-flow within the text column; the side sets its horizontal placement, and the
    // margin separates it from the verse on whichever edge it sits.
    alignSelf: referenceSide,
    textAlign:
      referenceSide === 'flex-start' ? 'left' : referenceSide === 'flex-end' ? 'right' : 'center',
    [referenceAbove ? 'marginBottom' : 'marginTop']: 32 * scale,
  }

  const referenceNode = reference?.show && referenceLine && (
    <p style={referenceStyle ?? undefined}>{referenceLine}</p>
  )

  return (
    <div ref={rootRef} className={`relative h-full w-full overflow-hidden ${className}`}>
      {/* Offscreen measurer for the auto-fit search; never visible, never hit-testable. */}
      <div
        ref={measureRef}
        aria-hidden
        className="pointer-events-none invisible absolute left-0 top-0"
        style={{ whiteSpace: 'normal' }}
      />
      {/* The window stays mounted across item → empty → item so its <video> background keeps
          looping instead of restarting every time the text is cleared. */}
      {scale > 0 && (item || showBackground) && (
        <div
          className="absolute flex flex-col overflow-hidden"
          style={{
            left: `${viewport.x}%`,
            top: `${viewport.y}%`,
            width: `${viewport.width}%`,
            height: `${viewport.height}%`,
            // Solid color fills the window; media types paint an <img>/<video> layer below instead.
            backgroundColor:
              text.background.type === 'solid' ? text.background.color : 'transparent',
            justifyContent: JUSTIFY[text.verticalAlign],
            alignItems: ALIGN[text.horizontalAlign],
            textAlign: text.horizontalAlign,
            padding: `${padding.top * scale}px ${padding.right * scale}px ${padding.bottom * scale}px ${padding.left * scale}px`,
          }}
        >
          <BackgroundLayer background={text.background} />
          {referenceAbove && referenceNode}
          {item && (
            <p
              style={{
                position: 'relative', // above the media background layer
                zIndex: 1,
                fontFamily: verseFamily,
                fontWeight: text.fontWeight,
                fontSize: fitSize ?? text.fontSizePx * scale,
                lineHeight: 1.2,
                letterSpacing: '-0.02em',
                color: text.textColor,
                whiteSpace: VERSE_WHITE_SPACE,
              }}
            >
              {item.text}
            </p>
          )}
          {!referenceAbove && referenceNode}
        </div>
      )}
    </div>
  )
}

/**
 * Paints an image or looping video behind the verse text, filling the text window. Renders
 * nothing for a solid background (the window's backgroundColor handles that) or a dangling
 * asset reference.
 */
function BackgroundLayer({ background }: { background: BackgroundConfig }) {
  if (background.type === 'solid' || !background.assetId) {
    return null
  }
  const src = mediaFileUrl(background.assetId)
  const common = 'absolute inset-0 h-full w-full object-cover'
  return background.type === 'motion' ? (
    <video
      key={src}
      src={src}
      className={common}
      style={{ zIndex: 0 }}
      autoPlay
      muted
      loop
      playsInline
    />
  ) : (
    <img key={src} src={src} alt="" className={common} style={{ zIndex: 0 }} />
  )
}
