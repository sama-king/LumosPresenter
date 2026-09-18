import type { LiveSlide } from '../../lib/live'
import type { ChapterDto } from '../../lib/types'

/**
 * Turns a verse selection into the items that go to the Live Queue.
 * Contiguous verses combine into one item; an item splits when its text
 * would exceed MAX_LIVE_CHARS (a character-count proxy for the display's
 * line limit — tune against the real projection output).
 */

/** Splitting threshold for one live item. */
export const MAX_LIVE_CHARS = 380

/** Detections at or above this confidence push to the displays automatically. */
export const AUTO_LIVE_THRESHOLD = 0.75

export interface QueueItem {
  id: string
  reference: string
  text: string
  book: string
  chapter: number
  verses: number[]
  translation: string
  source: 'manual' | 'auto'
}

/** A composed item as the shared live queue holds it. */
export function toSlide(item: QueueItem): LiveSlide {
  return { ...item, kind: 'scripture' }
}

/** The other direction: a scripture slide back to the shape this module composes with. */
export function fromSlide(slide: LiveSlide): QueueItem | null {
  if (slide.kind !== 'scripture') return null
  const { kind: _kind, ...rest } = slide
  return { ...rest, source: rest.source === 'auto' ? 'auto' : 'manual' }
}

export function formatReference(
  book: string,
  chapter: number,
  start: number,
  end: number,
): string {
  return start === end ? `${book} ${chapter}:${start}` : `${book} ${chapter}:${start}-${end}`
}

function contiguousRuns(numbers: number[]): number[][] {
  const sorted = [...new Set(numbers)].sort((a, b) => a - b)
  const runs: number[][] = []
  for (const n of sorted) {
    const run = runs[runs.length - 1]
    if (run && n === run[run.length - 1] + 1) run.push(n)
    else runs.push([n])
  }
  return runs
}

export function composeLiveItems(
  chapter: ChapterDto,
  selectedVerses: number[],
  source: 'manual' | 'auto' = 'manual',
): QueueItem[] {
  const byNumber = new Map(chapter.verses.map(v => [v.number, v]))
  const items: QueueItem[] = []

  for (const run of contiguousRuns(selectedVerses)) {
    let chunk: number[] = []
    let chunkText = ''

    const flush = () => {
      if (chunk.length === 0) return
      items.push({
        id: crypto.randomUUID(),
        reference: formatReference(chapter.book, chapter.chapter, chunk[0], chunk[chunk.length - 1]),
        text: chunkText,
        book: chapter.book,
        chapter: chapter.chapter,
        verses: chunk,
        translation: chapter.translation,
        source,
      })
      chunk = []
      chunkText = ''
    }

    for (const n of run) {
      const verse = byNumber.get(n)
      if (!verse) continue
      const candidate = chunkText === '' ? verse.text : `${chunkText} ${verse.text}`
      if (chunk.length > 0 && candidate.length > MAX_LIVE_CHARS) {
        flush()
        chunkText = verse.text
        chunk = [n]
      } else {
        chunkText = candidate
        chunk.push(n)
      }
    }
    flush()
  }

  return items
}
