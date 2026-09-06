/** URL that serves a background media asset's file bytes (image or video). */
export const mediaFileUrl = (id: string) => `/api/media/backgrounds/${id}/file`

/**
 * URL that streams a media-library item's bytes. Distinct from mediaFileUrl: library items
 * are linked from the operator's disk (media_library), backgrounds are copied into media/.
 */
export const mediaLibraryFileUrl = (id: string) => `/api/media/library/${id}/file`
