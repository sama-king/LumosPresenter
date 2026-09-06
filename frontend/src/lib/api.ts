import type {
  AddMediaResult,
  ApiBibleKeyStatus,
  AudioDevice,
  AudioLevel,
  BrowseResponse,
  ChapterDto,
  DisplayConfig,
  DisplayDto,
  DisplaysResponse,
  EasyWorshipImportResult,
  FontDto,
  LiveItemDto,
  MediaAssetDto,
  MediaLibraryItemDto,
  SaveSongBody,
  SearchResponse,
  SongDto,
  SongImportResult,
  SongSummaryDto,
  StatusDto,
  Translation,
} from './types'

async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  const res = await fetch(url, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  // Read the raw body once; several endpoints (start/stop/clear) return 200 with
  // an empty body, so we can't blindly call res.json().
  const raw = await res.text()
  const data = raw ? (JSON.parse(raw) as unknown) : undefined

  if (!res.ok) {
    const message =
      (data as { message?: string })?.message ?? `${method} ${url} failed (${res.status})`
    throw new Error(message)
  }
  return data as T
}

export const api = {
  getStatus: () => request<StatusDto>('GET', '/api/status'),
  getTranslations: () =>
    request<{ current: string; translations: Translation[] }>('GET', '/api/translations'),
  setTranslation: (code: string) =>
    request<{ translation: string }>('POST', `/api/translation/${encodeURIComponent(code)}`),

  getBooks: () => request<{ books: string[] }>('GET', '/api/scripture/books'),
  searchScripture: (q: string) =>
    request<SearchResponse>('GET', `/api/scripture/search?q=${encodeURIComponent(q)}`),
  getChapter: (book: string, chapter: number, translation?: string) =>
    request<ChapterDto>(
      'GET',
      `/api/scripture/chapter/${encodeURIComponent(book)}/${chapter}` +
        (translation ? `?translation=${encodeURIComponent(translation)}` : ''),
    ),

  goLive: (item: {
    reference: string
    text: string
    translation: string
    source?: string
    kind?: 'scripture' | 'song' | 'media'
    book?: string
    chapter?: number
    verseStart?: number
    verseEnd?: number
    mediaId?: string
    mediaKind?: 'image' | 'video'
    mediaLoop?: boolean
  }) => request<LiveItemDto>('POST', '/api/live', item),
  /** A display telling the server its non-looping video finished. Fire-and-forget. */
  mediaEnded: (id: string) =>
    request<{ accepted: boolean }>('POST', '/api/live/media/ended', { id }),
  getLive: () => request<LiveItemDto | undefined>('GET', '/api/live'),
  clearLive: () => request<void>('POST', '/api/live/clear'),

  startListening: () => request<void>('POST', '/api/listening/start'),
  stopListening: () => request<void>('POST', '/api/listening/stop'),
  getAudioDevices: () =>
    request<{ selected: number | null; devices: AudioDevice[] }>('GET', '/api/audio/devices'),
  setAudioDevice: (deviceId: number | null) =>
    request<void>('POST', '/api/audio/device', { deviceId }),
  getAudioLevel: () => request<AudioLevel>('GET', '/api/audio/level'),
  simulate: (text: string) => request<void>('POST', '/api/simulate', { text }),

  getNetwork: () =>
    request<{ host: string | null; ips: string[]; port: number }>('GET', '/api/network'),
  getFonts: () => request<{ fonts: FontDto[] }>('GET', '/api/fonts'),
  getDisplays: () => request<DisplaysResponse>('GET', '/api/displays'),
  getDisplay: (id: number) => request<DisplayDto>('GET', `/api/displays/${id}`),
  createDisplay: (body: { name?: string; useSettingsOfDisplayId?: number }) =>
    request<DisplayDto>('POST', '/api/displays', body),
  saveDisplayConfig: (id: number, config: DisplayConfig) =>
    request<DisplayDto>('PUT', `/api/displays/${id}/config`, config),
  setDisplaySource: (id: number, followsDisplayId: number | null) =>
    request<DisplayDto>('PUT', `/api/displays/${id}/source`, { followsDisplayId }),
  deleteDisplay: (id: number) => request<void>('DELETE', `/api/displays/${id}`),

  getMedia: () =>
    request<{ assets: MediaAssetDto[] }>('GET', '/api/media/backgrounds'),
  uploadMedia: async (file: File) => {
    // Multipart upload: let the browser set the boundary content-type (not JSON).
    const form = new FormData()
    form.append('file', file)
    const res = await fetch('/api/media/backgrounds', { method: 'POST', body: form })
    const raw = await res.text()
    const data = raw ? (JSON.parse(raw) as unknown) : undefined
    if (!res.ok) {
      throw new Error((data as { message?: string })?.message ?? `Upload failed (${res.status})`)
    }
    return data as MediaAssetDto
  },
  deleteMedia: (id: string) => request<void>('DELETE', `/api/media/backgrounds/${id}`),

  // Media library (the /media tab). Files are linked by absolute path, never uploaded —
  // browseMedia is how the console gets a real path, since a browser file input hides it.
  getMediaLibrary: () =>
    request<{ items: MediaLibraryItemDto[] }>('GET', '/api/media/library'),
  addMediaPaths: (paths: string[]) =>
    request<AddMediaResult>('POST', '/api/media/library', { paths }),
  deleteMediaLibraryItem: (id: string) => request<void>('DELETE', `/api/media/library/${id}`),
  browseMedia: (path?: string) =>
    request<BrowseResponse>(
      'GET',
      '/api/media/library/browse' + (path ? `?path=${encodeURIComponent(path)}` : ''),
    ),

  // The api.bible key is write-only: reads return whether one is set plus a masked hint.
  getApiBibleKey: () => request<ApiBibleKeyStatus>('GET', '/api/settings/api-bible'),
  setApiBibleKey: (key: string) =>
    request<ApiBibleKeyStatus>('POST', '/api/settings/api-bible', { key }),
  clearApiBibleKey: () => request<ApiBibleKeyStatus>('DELETE', '/api/settings/api-bible'),

  setParserWindow: (utterances: number) =>
    request<{ utteranceWindow: number }>('POST', `/api/parser/window/${utterances}`),
  // Sent as a percentage so the URL carries no decimal point.
  setAutoLiveConfidence: (percent: number) =>
    request<{ autoLiveConfidence: number }>('POST', `/api/parser/confidence/${percent}`),
  setEngine: (name: string) =>
    request<void>('POST', `/api/engine/${encodeURIComponent(name)}`),

  getWhisperModels: () =>
    request<{ selected: string; models: string[] }>('GET', '/api/whisper/models'),
  setWhisperModel: (model: string) =>
    request<{ selected: string }>('POST', '/api/whisper/model', { model }),

  getSongs: (q?: string) =>
    request<{ songs: SongSummaryDto[] }>(
      'GET',
      '/api/songs' + (q ? `?q=${encodeURIComponent(q)}` : ''),
    ),
  getSong: (id: number) => request<SongDto>('GET', `/api/songs/${id}`),
  createSong: (body: SaveSongBody) => request<SongDto>('POST', '/api/songs', body),
  updateSong: (id: number, body: SaveSongBody) => request<SongDto>('PUT', `/api/songs/${id}`, body),
  deleteSong: (id: number) => request<void>('DELETE', `/api/songs/${id}`),
  importSongTexts: async (files: File[]) => {
    // Multipart like uploadMedia: browser sets the boundary content-type (not JSON).
    const form = new FormData()
    for (const file of files) form.append('files', file)
    const res = await fetch('/api/songs/import/text', { method: 'POST', body: form })
    const raw = await res.text()
    const data = raw ? (JSON.parse(raw) as unknown) : undefined
    if (!res.ok) {
      throw new Error((data as { message?: string })?.message ?? `Import failed (${res.status})`)
    }
    return data as SongImportResult
  },
  importSongEasyWorship: async (file: File) => {
    const form = new FormData()
    form.append('file', file)
    const res = await fetch('/api/songs/import/easyworship', { method: 'POST', body: form })
    const raw = await res.text()
    const data = raw ? (JSON.parse(raw) as unknown) : undefined
    if (!res.ok) {
      throw new Error((data as { message?: string })?.message ?? `Import failed (${res.status})`)
    }
    return data as EasyWorshipImportResult
  },
}
