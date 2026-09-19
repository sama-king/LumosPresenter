import type { LiveSlide } from '../../lib/live'
import type { SongDto, SongSectionDto } from '../../lib/types'

/**
 * One projection slide of a song, staged for go-live. Unlike scripture's liveComposer,
 * there is NO character-count splitting: a section is authored as a slide, and the
 * display's auto-fit shrinks long ones. `reference` is operator-facing only — songs
 * carry no reference line on the projection surface.
 */
export interface SongQueueItem {
  id: string
  sectionPosition: number
  label: string | null
  reference: string
  text: string
}

/** Human label for a section: its own label, or "Section N" (1-based) when unlabeled. */
export function sectionLabel(section: SongSectionDto): string {
  return section.label ?? `Section ${section.position + 1}`
}

/** One queue item per section, in order. */
export function composeSongQueue(song: SongDto): SongQueueItem[] {
  return song.sections.map(section => ({
    id: `${song.id}:${section.position}`,
    sectionPosition: section.position,
    label: section.label,
    reference: `${song.title} — ${sectionLabel(section)}`,
    text: section.text,
  }))
}

/**
 * Client-side mirror of the server LyricsParser split for live editor preview: blank
 * line = new section. It does NOT reproduce label detection (the server owns that on
 * save); it only shows the operator how their lyrics will slice into slides.
 */
export function previewSections(lyrics: string): string[] {
  return lyrics
    .replace(/\r\n/g, '\n')
    .split(/\n\s*\n/)
    .map(block => block.trim())
    .filter(block => block.length > 0)
}

/** Sections back to editor text (label line + text, blank-line separated) — mirrors LyricsParser.Compose. */
export function sectionsToLyrics(sections: SongSectionDto[]): string {
  return sections
    .map(section => (section.label ? `${section.label}\n${section.text}` : section.text))
    .join('\n\n')
}

/** A song section as the shared live queue holds it. */
export function toSlide(song: SongDto, item: SongQueueItem): LiveSlide {
  return {
    id: item.id,
    kind: 'song',
    reference: item.reference,
    text: item.text,
    label: item.label,
    songId: song.id,
    sectionPosition: item.sectionPosition,
  }
}
