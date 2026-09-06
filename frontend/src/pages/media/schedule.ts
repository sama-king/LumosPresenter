import type { MediaLibraryItemDto } from '../../lib/types'

/**
 * The media schedule: what the operator has arranged for quick access during a service.
 *
 * Three entry shapes, and the distinction matters — slideshows and queues exist ONLY here,
 * never in the gallery. The gallery is the flat set of linked files; the schedule is where
 * those files get grouped and ordered for a service.
 *
 *  - 'item'      one gallery item (image or video)
 *  - 'slideshow' an ordered set of images advanced on a timer (secondsPerSlide)
 *  - 'queue'     an ordered set of videos played one after another
 *
 * Entries reference gallery items by id, not by value, so deleting a file from the gallery
 * leaves the schedule entry pointing at an id that no longer resolves — rendered as a
 * missing tile rather than silently vanishing mid-service.
 */

export type ScheduleEntry =
  | { id: string; type: 'item'; mediaId: string }
  | { id: string; type: 'slideshow'; title: string; mediaIds: string[]; secondsPerSlide: number }
  | { id: string; type: 'queue'; title: string; mediaIds: string[] }

/** Default dwell time for a new slideshow, in seconds. */
export const DEFAULT_SLIDE_SECONDS = 4

/** Bounds for the slideshow playtime control — a slide under a second is unreadable. */
export const MIN_SLIDE_SECONDS = 1
export const MAX_SLIDE_SECONDS = 120

const newId = () => `sch-${Math.random().toString(36).slice(2, 10)}`

export const scheduleItem = (mediaId: string): ScheduleEntry => ({
  id: newId(),
  type: 'item',
  mediaId,
})

export const scheduleSlideshow = (mediaIds: string[], title: string): ScheduleEntry => ({
  id: newId(),
  type: 'slideshow',
  title,
  mediaIds,
  secondsPerSlide: DEFAULT_SLIDE_SECONDS,
})

export const scheduleQueue = (mediaIds: string[], title: string): ScheduleEntry => ({
  id: newId(),
  type: 'queue',
  title,
  mediaIds,
})

/** Gallery ids an entry plays, in order — one for a plain item, many for a group. */
export function entryMediaIds(entry: ScheduleEntry): string[] {
  return entry.type === 'item' ? [entry.mediaId] : entry.mediaIds
}

/** Human label for a schedule row. Groups carry their own title; a plain item borrows the file's. */
export function entryTitle(entry: ScheduleEntry, byId: Map<string, MediaLibraryItemDto>): string {
  if (entry.type !== 'item') return entry.title
  return byId.get(entry.mediaId)?.title ?? 'Missing file'
}

/**
 * Names a new group from what's in it: the first item's title, plus how many follow.
 * Keeps the schedule readable without prompting the operator mid-service.
 */
export function groupTitle(
  mediaIds: string[],
  byId: Map<string, MediaLibraryItemDto>,
  kind: 'Slideshow' | 'Queue',
): string {
  const first = byId.get(mediaIds[0])?.title
  return first ? `${kind}: ${first} +${mediaIds.length - 1}` : `${kind} (${mediaIds.length})`
}
