/** A song that was sent live this session (whole songs, not individual sections). */
export interface SessionSong {
  id: number
  title: string
  author: string | null
  at: string // HH:MM
}
