import type {
  ContentType,
  DisplayConfig,
  ReferenceConfig,
  TextDisplayConfig,
  ViewportRect,
} from '../../lib/types'

/** Rail sections: the three content types plus the pinned settings-source entry. */
export type SectionId = ContentType | 'source'

/** Smallest allowed viewport edge, in percent of the frame. */
export const MIN_VIEWPORT_SIZE = 10

export type DragHandle = 'move' | 'n' | 's' | 'e' | 'w' | 'ne' | 'nw' | 'se' | 'sw'

export const WEIGHT_LABELS: Record<number, string> = {
  100: 'Thin',
  200: 'ExtraLight',
  300: 'Light',
  400: 'Regular',
  500: 'Medium',
  600: 'SemiBold',
  700: 'Bold',
  800: 'ExtraBold',
  900: 'Black',
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max)
}

function round1(value: number): number {
  return Math.round(value * 10) / 10
}

export function clampViewport(rect: ViewportRect): ViewportRect {
  const width = clamp(rect.width, MIN_VIEWPORT_SIZE, 100)
  const height = clamp(rect.height, MIN_VIEWPORT_SIZE, 100)
  return {
    x: round1(clamp(rect.x, 0, 100 - width)),
    y: round1(clamp(rect.y, 0, 100 - height)),
    width: round1(width),
    height: round1(height),
  }
}

/**
 * Applies a pointer drag (deltas in percent of the frame) to the rect that existed
 * when the drag started. Moving never resizes; edge handles constrain one axis;
 * resizing keeps the opposite edge fixed and respects the frame and minimum size.
 */
export function applyHandleDrag(
  start: ViewportRect,
  handle: DragHandle,
  dx: number,
  dy: number,
): ViewportRect {
  if (handle === 'move') {
    return {
      x: round1(clamp(start.x + dx, 0, 100 - start.width)),
      y: round1(clamp(start.y + dy, 0, 100 - start.height)),
      width: start.width,
      height: start.height,
    }
  }

  let { x, y, width, height } = start
  const right = start.x + start.width
  const bottom = start.y + start.height

  if (handle.includes('w')) {
    x = clamp(start.x + dx, 0, right - MIN_VIEWPORT_SIZE)
    width = right - x
  }
  if (handle.includes('e')) {
    width = clamp(start.width + dx, MIN_VIEWPORT_SIZE, 100 - start.x)
  }
  if (handle.includes('n')) {
    y = clamp(start.y + dy, 0, bottom - MIN_VIEWPORT_SIZE)
    height = bottom - y
  }
  if (handle.includes('s')) {
    height = clamp(start.height + dy, MIN_VIEWPORT_SIZE, 100 - start.y)
  }
  return { x: round1(x), y: round1(y), width: round1(width), height: round1(height) }
}

function rectsEqual(a: ViewportRect, b: ViewportRect): boolean {
  return a.x === b.x && a.y === b.y && a.width === b.width && a.height === b.height
}

function textsEqual(a: TextDisplayConfig, b: TextDisplayConfig): boolean {
  return (
    a.fontSlug === b.fontSlug &&
    a.fontWeight === b.fontWeight &&
    a.fontSizePx === b.fontSizePx &&
    a.textColor === b.textColor &&
    a.horizontalAlign === b.horizontalAlign &&
    a.verticalAlign === b.verticalAlign &&
    a.background.type === b.background.type &&
    a.background.color === b.background.color &&
    (a.background.assetId ?? null) === (b.background.assetId ?? null) &&
    rectsEqual(a.viewport, b.viewport) &&
    a.padding.top === b.padding.top &&
    a.padding.right === b.padding.right &&
    a.padding.bottom === b.padding.bottom &&
    a.padding.left === b.padding.left
  )
}

function referencesEqual(a: ReferenceConfig, b: ReferenceConfig): boolean {
  return (
    a.show === b.show &&
    a.position === b.position &&
    a.fontSlug === b.fontSlug &&
    a.fontWeight === b.fontWeight &&
    a.fontSizePx === b.fontSizePx &&
    a.color === b.color
  )
}

export function configsEqual(a: DisplayConfig, b: DisplayConfig): boolean {
  return (
    textsEqual(a.scripture.text, b.scripture.text) &&
    referencesEqual(a.scripture.reference, b.scripture.reference) &&
    textsEqual(a.songs.text, b.songs.text) &&
    a.media.fit === b.media.fit &&
    a.media.backgroundColor === b.media.backgroundColor &&
    a.media.audio === b.media.audio &&
    rectsEqual(a.media.viewport, b.media.viewport)
  )
}

/** The viewport a content type renders into (media's is top-level; text types nest it). */
export function viewportOf(config: DisplayConfig, type: ContentType): ViewportRect {
  return type === 'media' ? config.media.viewport : config[type].text.viewport
}
