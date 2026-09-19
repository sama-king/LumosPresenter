/**
 * A song sent live, or added from the library, this session (whole songs, not individual
 * sections).
 */
export interface SessionSong {
  id: number
  title: string
  author: string | null
  at: string // HH:MM
}
