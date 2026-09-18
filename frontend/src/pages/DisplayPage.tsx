import { useCallback, useEffect, useRef, useState } from 'react'
import { useParams } from 'react-router-dom'
import VerseCanvas from '../components/VerseCanvas'
import { api } from '../lib/api'
import { mediaLibraryFileUrl } from '../lib/media'
import { anchorTransport, useSyncedVideo, type TransportAnchor } from '../lib/mediaTransport'
import { useServerEvent, useServerReconnect } from '../lib/events'
import type {
  DisplayConfig,
  DisplayConfigEvent,
  FontDto,
  LiveBackdrop,
  LiveEvent,
  LiveItemDto,
  LiveSyncEvent,
  MediaTransportDto,
} from '../lib/types'

/**
 * Projection/OBS surface for one configured display (/display/:id). Renders whatever
 * the operator console (or the confidence-gated auto flow) pushed live, styled by the
 * display's stage configuration; config edits restyle it live via the 'displayconfig'
 * SSE event. Long verses keep their fixed font size and clip to the viewport —
 * auto-fit is future work.
 *
 * This surface only ever follows. It holds no queue, decides nothing about what comes next,
 * and does not run video playback of its own: the console owns one transport clock and this
 * page seeks to it, which is how several screens stay together. It reconciles against the
 * server on every reconnect and whenever the live heartbeat reports a revision it has not
 * applied, so a missed event cannot leave a screen showing last week's clip.
 */

export default function DisplayPage() {
  const { id } = useParams()
  const displayId = Number(id)

  const [item, setItem] = useState<LiveItemDto | null>(null)
  const [transport, setTransport] = useState<TransportAnchor | null>(null)
  // After a text-only clear: whose text-window background stays up with no words on it.
  const [backdrop, setBackdrop] = useState<LiveBackdrop | null>(null)
  const [config, setConfig] = useState<DisplayConfig | null>(null)
  const [fonts, setFonts] = useState<FontDto[]>([])
  const [notFound, setNotFound] = useState(false)
  const videoRef = useRef<HTMLVideoElement>(null)
  // Highest live-channel revision applied here. The heartbeat compares against it.
  const revision = useRef(0)

  // Sound only comes out of the display the operator designated for it, so a second output or
  // a confidence monitor does not play the clip a second time, slightly apart from the first.
  useSyncedVideo(
    videoRef,
    item?.kind === 'media' && item.id === transport?.itemId ? transport : null,
    { silent: config?.media.audio === false },
  )

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

  const applyTransport = useCallback((dto: MediaTransportDto | null) => {
    setTransport(previous => (dto === null ? null : anchorTransport(previous, dto)))
  }, [])

  /** Re-reads the whole live channel — how this display catches up, however it fell behind. */
  const resync = useCallback(() => {
    void api
      .getLive()
      .then(snapshot => {
        // An event that landed while this was in flight is newer than what came back.
        if (snapshot.revision < revision.current) return
        revision.current = snapshot.revision
        setItem(snapshot.item)
        setBackdrop(snapshot.item ? null : (snapshot.backdrop ?? null))
        applyTransport(snapshot.transport)
      })
      .catch(() => {})
  }, [applyTransport])

  // Late-join: pick up the display config and whatever is already live.
  useEffect(() => {
    void Promise.all([api.getDisplay(displayId), api.getFonts()])
      .then(([display, fontsData]) => {
        setConfig(display.config)
        setFonts(fontsData.fonts)
      })
      .catch(() => setNotFound(true))
    resync()
  }, [displayId, resync])

  // An EventSource does not replay what it missed while it was away, so re-reading on every
  // reconnect is the only thing that ends a stale picture. (The mount case is covered above:
  // navigating here in an app that is already connected sees no fresh 'open'.)
  useServerReconnect(resync)

  useServerEvent<LiveEvent>('live', data => {
    revision.current = Math.max(revision.current, data.revision)
    if ('cleared' in data) {
      setItem(null)
      setTransport(null)
      setBackdrop(data.backdrop ?? null)
      return
    }
    setItem(data)
    setBackdrop(null)
    // A push that is not a video carries no transport; dropping it here keeps a stale clock
    // from driving whatever comes next.
    if (data.kind !== 'media' || data.mediaKind !== 'video') setTransport(null)
  })

  useServerEvent<MediaTransportDto>('mediatransport', data => {
    revision.current = Math.max(revision.current, data.revision)
    applyTransport(data)
  })

  // The heartbeat: a revision this display never applied means it missed an event, so re-read
  // rather than keep projecting the wrong thing. A matching revision still refreshes the
  // transport, which is what re-checks (and corrects) video drift against the console.
  useServerEvent<LiveSyncEvent>('livesync', data => {
    if (data.revision !== revision.current) {
      resync()
      return
    }
    if (data.transport) applyTransport(data.transport)
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
            // No autoplay, loop, muted or volume attributes: all of them are set from the
            // console's transport (useSyncedVideo above), so this screen plays where the
            // console says it is rather than wherever its own decode happened to start.
            // The key is the live-item id, so re-sending the same file is a fresh element
            // starting from the transport's position rather than a clip left mid-play.
            <video
              key={item.id}
              ref={videoRef}
              src={src}
              playsInline
              preload="auto"
              onEnded={() => {
                // Only a non-looping clip ends, and only a queue member is non-looping: the
                // report is what lets the console play the next one. Every display showing
                // the item reports; the console collapses them to a single advance.
                if (!(transport?.loop ?? item.mediaLoop)) void api.mediaEnded(item.id).catch(() => {})
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
  // (scripture, free-form pushes) uses the scripture config. With nothing live, a text-only
  // clear leaves the backdrop's window up — same config, so the background carries straight on.
  const textType = item ? (item.kind === 'song' ? 'songs' : 'scripture') : (backdrop ?? 'scripture')

  return (
    <main className="h-screen w-screen overflow-hidden">
      <VerseCanvas
        text={config[textType].text}
        reference={textType === 'songs' ? null : config.scripture.reference}
        fonts={fonts}
        item={item}
        showBackground={backdrop !== null}
      />
    </main>
  )
}
