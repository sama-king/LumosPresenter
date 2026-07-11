// DTOs mirroring the WebHost API payloads (see src/LumosPresenter.WebHost/Program.cs
// and Realtime/TranscriptionPipeline.cs — minimal APIs serialize camelCase).

export interface Translation {
  id: string
  name: string
  language: string
}

export interface StatusDto {
  listening: boolean
  engine: string
  engines: string[]
  deviceId: number | null
  recording: boolean
  lastRecording: string | null
  utteranceWindow: number
  translation: string
}

export interface AudioLevel {
  peak: number
  rms: number
  clipping: boolean
}

export interface AudioDevice {
  id: number
  name: string
  maxInputChannels: number
  isDefault: boolean
}

export interface TranscriptEvent {
  text: string
  isFinal: boolean
  source: string
  at: string
}

export interface ReferenceEvent {
  display: string
  book: string
  chapter: number
  verseStart: number | null
  verseEnd: number | null
  confidence: number
  utterance: string
  translation: string
  text: string
}

export interface VerseDto {
  number: number
  text: string
}

export interface ChapterDto {
  translation: string
  book: string
  bookNumber: number
  chapter: number
  chapterCount: number
  verses: VerseDto[]
}

export interface SearchResultDto {
  display: string
  book: string
  chapter: number
  verseStart: number | null
  verseEnd: number | null
  confidence: number
  verses: VerseDto[]
}

export interface SearchResponse {
  query: string
  translation: string
  results: SearchResultDto[]
}

export interface LiveItemDto {
  id: string
  reference: string
  text: string
  translation: string
  source: string
  at: string
  /** Which config block the display styles this with; 'scripture' for legacy/free-form pushes. */
  kind: 'scripture' | 'song'
  /** Structured reference; null for free-form pushes (see LiveState.LiveItem). */
  book: string | null
  chapter: number | null
  verseStart: number | null
  verseEnd: number | null
}

/** SSE `live` event payload: a live item, or a clear marker. */
export type LiveEvent = LiveItemDto | { cleared: true }

// --- Stage configuration (per-display styling of the live feed) ---

export interface FontDto {
  slug: string
  name: string
  cssFamily: string
  source: string // 'bundled' | 'file' (future user-installed)
  cssUrl: string | null
  weights: number[]
  enabled: boolean
  sortOrder: number
}

/** Text viewport within the 1920x1080 frame, in percent (0..100). */
export interface ViewportRect {
  x: number
  y: number
  width: number
  height: number
}

export interface BackgroundConfig {
  type: 'solid' | 'image' | 'motion'
  color: string
  /** References a MediaAssetDto when type is 'image' or 'motion'; null/absent for 'solid'. */
  assetId?: string | null
}

/** A background media asset (uploaded image or looping video). Mirrors the backend MediaAsset. */
export interface MediaAssetDto {
  id: string
  kind: 'image' | 'motion'
  title: string
  fileExt: string
  contentType: string
  source: string // 'bundled' | 'file'
  sortOrder: number
}

/** Inner padding of the text viewport, in px at 1920-frame scale. */
export interface PaddingConfig {
  top: number
  right: number
  bottom: number
  left: number
}

// Always relative to the verse text: {above|below} flows the reference line above or below
// the verse block, {left|center|right} aligns it horizontally within the text column.
export type ReferencePosition =
  | 'above-left'
  | 'above-center'
  | 'above-right'
  | 'below-left'
  | 'below-center'
  | 'below-right'

export interface ReferenceConfig {
  show: boolean
  position: ReferencePosition
  fontSlug: string
  fontWeight: number
  fontSizePx: number
  color: string
}

/**
 * Shared text-rendering block: how verse/lyric text is drawn in its own viewport.
 * Embedded by the scripture and songs configs so each content type sizes and styles independently.
 */
export interface TextDisplayConfig {
  fontSlug: string
  fontWeight: number
  /** Maximum size: the display auto-fits text down from here to fill the viewport. */
  fontSizePx: number
  textColor: string
  horizontalAlign: 'left' | 'center' | 'right'
  verticalAlign: 'top' | 'middle' | 'bottom'
  background: BackgroundConfig
  viewport: ViewportRect
  padding: PaddingConfig
}

/** Scripture target settings: verse text plus the reference line (scripture-only concept). */
export interface ScriptureDisplayConfig {
  text: TextDisplayConfig
  reference: ReferenceConfig
}

/** Song lyrics target settings: text only, no reference line. */
export interface SongsDisplayConfig {
  text: TextDisplayConfig
}

/** Media (image/video) target settings. backgroundColor fills the window when idle or letterboxing. */
export interface MediaDisplayConfig {
  fit: 'cover' | 'contain'
  backgroundColor: string
  viewport: ViewportRect
}

/** The content types a display can render; each has its own config (and rail tab). */
export type ContentType = 'scripture' | 'songs' | 'media'

/** Per-display settings container: one independent config per content type. */
export interface DisplayConfig {
  scripture: ScriptureDisplayConfig
  songs: SongsDisplayConfig
  media: MediaDisplayConfig
}

/** config is always the effective config (the source display's when followsDisplayId is set). */
export interface DisplayDto {
  id: number
  name: string
  sortOrder: number
  config: DisplayConfig
  followsDisplayId: number | null
}

export interface DisplaysResponse {
  defaultConfig: DisplayConfig
  displays: DisplayDto[]
}

/** SSE `displayconfig` event payload; followers receive one under their own id. */
export interface DisplayConfigEvent {
  displayId: number
  config: DisplayConfig
}

// --- Song library ---

/** Library listing row (search results); lyrics are loaded on demand via getSong. */
export interface SongSummaryDto {
  id: number
  title: string
  author: string | null
}

/** One projection slide of a song. */
export interface SongSectionDto {
  position: number
  label: string | null
  text: string
}

/** A full song with its ordered sections. */
export interface SongDto {
  id: number
  title: string
  author: string | null
  copyright: string | null
  sections: SongSectionDto[]
}

/** Save payload: the server parses `lyrics` into sections (blank line = new slide). */
export interface SaveSongBody {
  title: string
  author?: string | null
  copyright?: string | null
  lyrics: string
}

/** Result of a .txt import batch: each file succeeds or fails independently. */
export interface SongImportResult {
  imported: { id: number; title: string }[]
  errors: { file: string; message: string }[]
}

/** Result of an EasyWorship database import: imported, skipped (existing title), per-song errors. */
export interface EasyWorshipImportResult {
  imported: { id: number; title: string }[]
  skipped: string[]
  errors: { title: string; message: string }[]
}
