import { useEffect, useState } from 'react'
import { useParams } from 'react-router-dom'
import VerseCanvas from '../components/VerseCanvas'
import { api } from '../lib/api'
import { mediaLibraryFileUrl } from '../lib/media'
import { useServerEvent } from '../lib/events'
import type { DisplayConfig, DisplayConfigEvent, FontDto, LiveEvent, LiveItemDto } from '../lib/types'

/**
 * Projection/OBS surface for one configured display (/display/:id). Renders whatever
 * the operator console (or the confidence-gated auto flow) pushed live, styled by the
 * display's stage configuration; config edits restyle it live via the 'displayconfig'
 * SSE event. Long verses keep their fixed font size and clip to the viewport —
 * auto-fit is future work.
 */

export default function DisplayPage() {
  const { id } = useParams()
  const displayId = Number(id)

  const [item, setItem] = useState<LiveItemDto | null>(null)
  const [config, setConfig] = useState<DisplayConfig | null>(null)
  const [fonts, setFonts] = useState<FontDto[]>([])
  const [notFound, setNotFound] = useState(false)

  // Only the text viewport is painted: unpainted pixels read as alpha in OBS /
  // compositing contexts. color-scheme keeps a plain fullscreen browser dark
  // (its default canvas) instead of flashing white.
  useEffect(() => {
    const body = document.body
    const root = document.documentElement
    const previousBackground = body.style.background
    const previousScheme = root.style.colorScheme
    body.style.background = 'transparent'
    root.style.colorScheme = 'dark'
    return () => {
      body.style.background = previousBackground
      root.style.colorScheme = previousScheme
    }
  }, [])

  // Late-join: pick up the display config and whatever is already live.
  useEffect(() => {
    void Promise.all([api.getDisplay(displayId), api.getFonts(), api.getLive()])
      .then(([display, fontsData, live]) => {
        setConfig(display.config)
        setFonts(fontsData.fonts)
        setItem(live ?? null)
      })
      .catch(() => setNotFound(true))
  }, [displayId])

  useServerEvent<LiveEvent>('live', data => {
    setItem('cleared' in data ? null : data)
  })

  useServerEvent<DisplayConfigEvent>('displayconfig', data => {
    // Followers get their own event from the server, so no link-awareness needed here.
    if (data.displayId === displayId) setConfig(data.config)
  })

  if (notFound) {
    return (
      <main className="flex h-screen items-center justify-center bg-navy-deep">
        <p className="font-mono text-status-label uppercase text-slate-muted">
          Display {id} not found
        </p>
      </main>
    )
  }

  // While config loads, paint nothing — same as the idle (transparent) state.
  if (config === null) {
    return <main className="h-screen w-screen" />
  }

  // Media is a picture, not text: it renders in its own viewport with the media config's
  // fit and background rather than through VerseCanvas.
  if (item?.kind === 'media' && item.mediaId) {
    const { fit, backgroundColor, viewport } = config.media
    const src = mediaLibraryFileUrl(item.mediaId)
    return (
      // relative: the viewport percentages below are anchored to this frame, not the page.
      <main className="relative h-screen w-screen overflow-hidden">
        <div
          style={{
            position: 'absolute',
            left: `${viewport.x}%`,
            top: `${viewport.y}%`,
            width: `${viewport.width}%`,
            height: `${viewport.height}%`,
            backgroundColor,
          }}
        >
          {item.mediaKind === 'video' ? (
            // Autoplay needs muted. A standalone clip loops; a queue member does not, and
            // instead reports back when it ends so the console can play the next one. Every
            // display showing this item reports — the console de-duplicates by item id.
            <video
              key={item.mediaId}
              src={src}
              autoPlay
              muted
              loop={item.mediaLoop}
              playsInline
              onEnded={() => {
                if (!item.mediaLoop) void api.mediaEnded(item.id).catch(() => {})
              }}
              style={{ width: '100%', height: '100%', objectFit: fit }}
            />
          ) : (
            <img
              key={item.mediaId}
              src={src}
              alt=""
              style={{ width: '100%', height: '100%', objectFit: fit }}
            />
          )}
        </div>
      </main>
    )
  }

  // Songs style with their own text block and carry no reference line; everything else
  // (scripture, free-form pushes) uses the scripture config.
  const isSong = item?.kind === 'song'

  return (
    <main className="h-screen w-screen overflow-hidden">
      <VerseCanvas
        text={isSong ? config.songs.text : config.scripture.text}
        reference={isSong ? null : config.scripture.reference}
        fonts={fonts}
        item={item}
      />
    </main>
  )
}
