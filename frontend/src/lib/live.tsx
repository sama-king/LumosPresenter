import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { api } from './api'
import { useServerEvent, useServerReconnect } from './events'
import { anchorTransport, transportPosition, type TransportAnchor } from './mediaTransport'
import type {
  LiveBackdrop,
  LiveEvent,
  LiveItemDto,
  LiveSyncEvent,
  MediaEndedEvent,
  MediaTransportDto,
} from './types'

/**
 * The live channel, owned in one place for the whole app.
 *
 * There is exactly one thing on the displays at a time, so there is exactly one live queue —
 * not one per tab. Scripture, songs and media all hand slides to this controller and read what
 * is live back from it, which is why the panel on the right is the same panel everywhere: what
 * it shows IS what the displays show, whichever tab put it there. Anything that needs to know
 * what is live (video transport, queue advance, foreign pushes from the voice pipeline) is
 * decided here rather than three times over.
 */

export type LiveOrigin = 'scripture' | 'songs' | 'media'

/** One slide in the live queue. The kinds mirror what a display knows how to style. */
export type LiveSlide =
  | {
      id: string
      kind: 'scripture'
      reference: string
      text: string
      translation: string
      source: string
      book: string
      chapter: number
      verses: number[]
    }
  | {
      id: string
      kind: 'song'
      reference: string
      text: string
      label: string | null
      songId: number
      sectionPosition: number
    }
  | {
      id: string
      kind: 'media'
      reference: string
      title: string
      mediaId: string
      mediaKind: 'image' | 'video'
    }

/**
 * How the queue moves on by itself:
 *  - 'manual' the operator steps it (scripture, songs, a single media item)
 *  - 'timer'  a slideshow, dwelling secondsPerSlide on each frame
 *  - 'end'    a video queue, advancing when the clip finishes
 *
 * `wrap` decides what running off the end does. A slideshow is meant to run unattended, so it
 * starts over; a video queue is a sequence with an end, and holds on the last clip.
 */
export type LiveAdvance =
  | { mode: 'manual' }
  | { mode: 'timer'; secondsPerSlide: number; wrap: boolean }
  | { mode: 'end'; wrap: boolean }

export interface LiveQueueInput {
  slides: LiveSlide[]
  /** Slide to put on the displays; defaults to the first. Pass null to stage without pushing. */
  liveId?: string | null
  origin: LiveOrigin
  title: string
  advance?: LiveAdvance
  /** Start the slideshow timer running. Ignored unless `advance` is a timer. */
  autoPlay?: boolean
}

interface LiveController {
  slides: LiveSlide[]
  liveId: string | null
  liveSlide: LiveSlide | null
  origin: LiveOrigin | null
  title: string
  advance: LiveAdvance
  /** Whether the slideshow timer is running. Always false outside a timer queue. */
  autoPlaying: boolean
  /** What the server says is live — including pushes this console did not make. */
  item: LiveItemDto | null
  /** Live-item id of what is on the displays; the target of transport and end reports. */
  liveItemId: string | null
  /** The live video's clock, or null when the live item is not a video. */
  transport: TransportAnchor | null
  /** Replaces the queue and (unless liveId is null) sends a slide to the displays. */
  setQueue: (input: LiveQueueInput) => void
  /**
   * Replaces the queue to mirror a push the SERVER already made — auto-live, or a re-push
   * after a translation switch. Nothing goes to the displays: they are already showing it,
   * and pushing again would fight the thing being mirrored.
   */
  adoptQueue: (input: LiveQueueInput & { liveId: string }) => void
  /** Sends one slide of the current queue to the displays. */
  show: (slideId: string) => void
  /** Moves the live position within the queue, honouring the queue's wrap rule. */
  step: (delta: number) => void
  removeSlide: (slideId: string) => void
  /**
   * Takes the words off the displays but leaves the text window's background up (a looping
   * video keeps playing). The queue stays, so any slide can be brought straight back.
   */
  clearText: () => void
  /** Takes everything off the displays — they go fully transparent — and empties the queue. */
  clearAll: () => void
  /** Same as clearAll; kept for callers that just need the displays emptied. */
  clear: () => void
  /** The background left up by a text-only clear, or null when nothing is showing. */
  backdrop: LiveBackdrop | null
  setAutoPlaying: (playing: boolean) => void
  setSecondsPerSlide: (seconds: number) => void
  /** Video transport — these reach every display, not just this console's monitor. */
  playPauseVideo: () => void
  stopVideo: () => void
  seekVideo: (seconds: number) => void
  setVideoLoop: (loop: boolean) => void
  /** Program level and mute — every display set to carry audio follows these. */
  setVideoVolume: (volume: number) => void
  setVideoMuted: (muted: boolean) => void
  /** A video finished. Advances a video queue; ignored for anything else. */
  reportVideoEnded: (itemId: string) => void
  error: string
}

const LiveContext = createContext<LiveController | null>(null)

const MANUAL: LiveAdvance = { mode: 'manual' }

/** Ids of our own recent pushes, to tell them from a push made elsewhere. */
const OWN_PUSH_MEMORY = 32

const newPushId = () => `p-${Math.random().toString(36).slice(2, 12)}`

/** Does this queue slide represent the item the server says is live? */
function matchesItem(slide: LiveSlide, item: LiveItemDto): boolean {
  if (slide.kind !== item.kind) return false
  if (slide.kind === 'media') return slide.mediaId === item.mediaId
  return slide.reference === item.reference && slide.text === item.text
}

/** Turns a push made elsewhere (voice pipeline, another console) into a one-slide queue. */
function reflectItem(item: LiveItemDto): { slide: LiveSlide; origin: LiveOrigin } | null {
  if (item.kind === 'media') {
    if (!item.mediaId) return null
    return {
      origin: 'media',
      slide: {
        id: `reflect-${item.id}`,
        kind: 'media',
        reference: item.reference,
        title: item.reference || 'Media',
        mediaId: item.mediaId,
        mediaKind: item.mediaKind ?? 'image',
      },
    }
  }
  if (item.kind === 'song') {
    return {
      origin: 'songs',
      slide: {
        id: `reflect-${item.id}`,
        kind: 'song',
        reference: item.reference,
        text: item.text,
        label: null,
        songId: -1,
        sectionPosition: 0,
      },
    }
  }
  return {
    origin: 'scripture',
    slide: {
      id: `reflect-${item.id}`,
      kind: 'scripture',
      reference: item.reference,
      text: item.text,
      translation: item.translation,
      source: item.source,
      book: item.book ?? '',
      chapter: item.chapter ?? 0,
      verses: item.verseStart === null ? [] : [item.verseStart],
    },
  }
}

export function LiveProvider({ children }: { children: ReactNode }) {
  const [slides, setSlides] = useState<LiveSlide[]>([])
  const [liveId, setLiveId] = useState<string | null>(null)
  const [origin, setOrigin] = useState<LiveOrigin | null>(null)
  const [title, setTitle] = useState('')
  const [advance, setAdvance] = useState<LiveAdvance>(MANUAL)
  const [autoPlaying, setAutoPlayingState] = useState(false)
  const [item, setItem] = useState<LiveItemDto | null>(null)
  const [transport, setTransport] = useState<TransportAnchor | null>(null)
  // The live-item id of the slide currently on the displays, which is what transport
  // commands and end-reports are matched against.
  const [liveItemId, setLiveItemId] = useState<string | null>(null)
  const [backdrop, setBackdrop] = useState<LiveBackdrop | null>(null)
  // The slide that was live when its text was cleared, so Next/Previous carry on from where
  // the operator was rather than from the top of the queue.
  const [clearedFromId, setClearedFromId] = useState<string | null>(null)
  const [error, setError] = useState('')

  // Ids of pushes this console made. A 'live' event carrying one of them is our own work
  // coming back; anything else took the displays away from us and rebuilds the queue.
  const ownPushes = useRef<string[]>([])
  // Highest revision applied. The heartbeat compares against it to catch a missed event.
  const revision = useRef(0)
  // Item ids whose end has already advanced the queue. Every display showing a clip reports
  // it, and the console monitor does too, so without this a two-screen setup would skip.
  const endedItems = useRef<string[]>([])

  const showError = useCallback((message: string) => {
    setError(message)
    window.setTimeout(() => setError(''), 6000)
  }, [])

  const rememberPush = useCallback((pushId: string) => {
    ownPushes.current = [...ownPushes.current, pushId].slice(-OWN_PUSH_MEMORY)
  }, [])

  const applyTransport = useCallback((dto: MediaTransportDto | null) => {
    setTransport(previous => (dto === null ? null : anchorTransport(previous, dto)))
  }, [])

  /**
   * Sends one slide to the displays. The live-item id is generated HERE and passed to the
   * server rather than read back from the response: the 'live' event can beat the response
   * to this browser, and a console that could not yet recognise its own push would treat it
   * as someone else's and tear its own queue down mid-gesture.
   */
  const push = useCallback(
    (slide: LiveSlide, mode: LiveAdvance) => {
      const pushId = newPushId()
      rememberPush(pushId)
      setLiveItemId(pushId)
      setLiveId(slide.id)
      setClearedFromId(null)

      const body =
        slide.kind === 'media'
          ? {
              id: pushId,
              reference: slide.title,
              text: '',
              translation: '',
              kind: 'media' as const,
              mediaId: slide.mediaId,
              mediaKind: slide.mediaKind,
              // A queue member must be allowed to end so the next clip can follow; anything
              // else repeats until the operator moves on.
              mediaLoop: mode.mode !== 'end',
            }
          : slide.kind === 'song'
            ? {
                id: pushId,
                reference: slide.reference,
                text: slide.text,
                translation: '',
                source: 'manual',
                kind: 'song' as const,
              }
            : {
                id: pushId,
                reference: slide.reference,
                text: slide.text,
                translation: slide.translation,
                source: slide.source,
                kind: 'scripture' as const,
                book: slide.book,
                chapter: slide.chapter,
                verseStart: slide.verses[0],
                verseEnd: slide.verses[slide.verses.length - 1],
              }

      void api.goLive(body).catch((err: Error) => showError(err.message))
    },
    [rememberPush, showError],
  )

  const setQueue = useCallback(
    (input: LiveQueueInput) => {
      const mode = input.advance ?? MANUAL
      setSlides(input.slides)
      setOrigin(input.origin)
      setTitle(input.title)
      setAdvance(mode)
      setAutoPlayingState(mode.mode === 'timer' && (input.autoPlay ?? true))
      const target =
        input.liveId === null
          ? null
          : (input.slides.find(slide => slide.id === input.liveId) ?? input.slides[0] ?? null)
      if (target) {
        push(target, mode)
      } else {
        setLiveId(null)
      }
    },
    [push],
  )

  const adoptQueue = useCallback((input: LiveQueueInput & { liveId: string }) => {
    const mode = input.advance ?? MANUAL
    setSlides(input.slides)
    setOrigin(input.origin)
    setTitle(input.title)
    setAdvance(mode)
    setAutoPlayingState(false)
    setLiveId(input.liveId)
  }, [])

  const show = useCallback(
    (slideId: string) => {
      const slide = slides.find(s => s.id === slideId)
      if (slide) push(slide, advance)
    },
    [slides, advance, push],
  )

  const step = useCallback(
    (delta: number) => {
      if (slides.length === 0) return
      const current = slides.findIndex(slide => slide.id === (liveId ?? clearedFromId))
      const next = (current < 0 ? 0 : current) + delta
      const wrap = advance.mode !== 'manual' && advance.wrap
      if (next < 0 || next >= slides.length) {
        if (!wrap) return
        const wrapped = ((next % slides.length) + slides.length) % slides.length
        push(slides[wrapped], advance)
        return
      }
      push(slides[next], advance)
    },
    [slides, liveId, clearedFromId, advance, push],
  )

  const removeSlide = useCallback((slideId: string) => {
    // Removing what is live leaves the displays alone — pulling the picture out from under
    // a service because a row was tidied would be worse than a stale highlight.
    setSlides(previous => previous.filter(slide => slide.id !== slideId))
  }, [])

  const resetQueue = useCallback(() => {
    setSlides([])
    setLiveId(null)
    setOrigin(null)
    setTitle('')
    setAdvance(MANUAL)
    setAutoPlayingState(false)
    setLiveItemId(null)
    setClearedFromId(null)
  }, [])

  /** Drops the live pointers but keeps the queue — the state after a text-only clear. */
  const releaseLive = useCallback(() => {
    if (liveId !== null) setClearedFromId(liveId)
    setLiveId(null)
    setLiveItemId(null)
    setAutoPlayingState(false)
  }, [liveId])

  const clearAll = useCallback(() => {
    resetQueue()
    setTransport(null)
    setBackdrop(null)
    void api.clearLive('all').catch((err: Error) => showError(err.message))
  }, [resetQueue, showError])

  const clearText = useCallback(() => {
    releaseLive()
    setTransport(null)
    void api.clearLive('text').catch((err: Error) => showError(err.message))
  }, [releaseLive, showError])

  const setAutoPlaying = useCallback((playing: boolean) => setAutoPlayingState(playing), [])

  const setSecondsPerSlide = useCallback((seconds: number) => {
    setAdvance(previous =>
      previous.mode === 'timer' ? { ...previous, secondsPerSlide: seconds } : previous,
    )
  }, [])

  // --- Video transport. Every command names the live item so a late click on something the
  //     operator has already moved past is dropped rather than seeking whatever is up now. ---

  const sendTransport = useCallback(
    (next: {
      playing: boolean
      position: number
      loop?: boolean
      volume?: number
      muted?: boolean
    }) => {
      const itemId = liveItemId
      if (!itemId) return
      // Optimistic: the operator sees the monitor respond now, and the server's echo (which
      // every display also gets) confirms it a round trip later.
      setTransport(previous =>
        previous && previous.itemId === itemId
          ? {
              ...previous,
              playing: next.playing,
              position: next.position,
              loop: next.loop ?? previous.loop,
              volume: next.volume ?? previous.volume,
              muted: next.muted ?? previous.muted,
              anchoredAt: performance.now(),
              tick: previous.tick + 1,
            }
          : previous,
      )
      void api.setMediaTransport({ itemId, ...next }).catch((err: Error) => showError(err.message))
    },
    [liveItemId, showError],
  )

  const playPauseVideo = useCallback(() => {
    if (!transport) return
    sendTransport({ playing: !transport.playing, position: transportPosition(transport) })
  }, [transport, sendTransport])

  const stopVideo = useCallback(() => {
    if (!transport) return
    sendTransport({ playing: false, position: 0 })
  }, [transport, sendTransport])

  const seekVideo = useCallback(
    (seconds: number) => {
      if (!transport) return
      sendTransport({ playing: transport.playing, position: Math.max(0, seconds) })
    },
    [transport, sendTransport],
  )

  const setVideoLoop = useCallback(
    (loop: boolean) => {
      if (!transport) return
      sendTransport({ playing: transport.playing, position: transportPosition(transport), loop })
    },
    [transport, sendTransport],
  )

  /** Program level — what the room hears, on every display set to carry audio. */
  const setVideoVolume = useCallback(
    (volume: number) => {
      if (!transport) return
      sendTransport({
        playing: transport.playing,
        position: transportPosition(transport),
        volume: Math.min(1, Math.max(0, volume)),
        // Moving the fader off zero is how an operator un-mutes; making them clear the mute
        // separately would look like a broken control.
        muted: volume <= 0 ? transport.muted : false,
      })
    },
    [transport, sendTransport],
  )

  const setVideoMuted = useCallback(
    (muted: boolean) => {
      if (!transport) return
      sendTransport({
        playing: transport.playing,
        position: transportPosition(transport),
        muted,
      })
    },
    [transport, sendTransport],
  )

  const reportVideoEnded = useCallback(
    (itemId: string) => {
      if (itemId !== liveItemId) return
      if (endedItems.current.includes(itemId)) return
      endedItems.current = [...endedItems.current, itemId].slice(-OWN_PUSH_MEMORY)
      if (advance.mode !== 'end') return
      step(1)
    },
    [liveItemId, advance, step],
  )

  // Slideshow advance. The console owns the timer so what is live is decided in one place;
  // the interval restarts whenever the dwell time or position changes, which is also why the
  // dwell time is read from `advance` rather than captured when the slideshow started.
  useEffect(() => {
    if (!autoPlaying || advance.mode !== 'timer' || slides.length < 2) return
    const handle = window.setTimeout(
      () => step(1),
      Math.max(1, advance.secondsPerSlide) * 1000,
    )
    return () => window.clearTimeout(handle)
  }, [autoPlaying, advance, slides.length, liveId, step])

  // --- Staying in step with the server ---

  /** Re-reads the whole live channel. The cure for anything missed, however it was missed. */
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

  useServerReconnect(resync)

  useServerEvent<LiveEvent>('live', event => {
    revision.current = Math.max(revision.current, event.revision)
    if ('cleared' in event) {
      setItem(null)
      setTransport(null)
      setBackdrop(event.backdrop ?? null)
      // A text-only clear keeps every console's queue so the operator can bring a slide back;
      // only a full clear empties it.
      if (event.scope === 'text') releaseLive()
      else resetQueue()
      return
    }
    setItem(event)
    setBackdrop(null)
    if (ownPushes.current.includes(event.id)) return

    // Someone else took the displays: the voice pipeline's auto-live, a translation re-push,
    // or another console. Keep the queue if it contains what is now live (the page that owns
    // it usually recomposes right after) and otherwise reflect the push as a one-slide queue,
    // so the panel always describes what is actually on screen.
    setLiveItemId(event.id)
    const match = slides.find(slide => matchesItem(slide, event))
    if (match) {
      setLiveId(match.id)
      return
    }
    const reflected = reflectItem(event)
    if (!reflected) return
    setSlides([reflected.slide])
    setLiveId(reflected.slide.id)
    setOrigin(reflected.origin)
    setTitle(event.reference)
    setAdvance(MANUAL)
    setAutoPlayingState(false)
  })

  useServerEvent<MediaTransportDto>('mediatransport', event => {
    revision.current = Math.max(revision.current, event.revision)
    applyTransport(event)
  })

  // A display reporting that its clip finished. Harmless duplication with the console
  // monitor's own end — reportVideoEnded advances once per item.
  useServerEvent<MediaEndedEvent>('mediaended', event => reportVideoEnded(event.id))

  // The heartbeat. A revision we have not applied means an event was missed — dropped from a
  // full fan-out buffer, or lost to a reconnect — so re-read rather than sit on a stale
  // picture. Matching revisions still refresh the transport, which re-checks video drift.
  useServerEvent<LiveSyncEvent>('livesync', event => {
    if (event.revision !== revision.current) {
      resync()
      return
    }
    if (event.transport) applyTransport(event.transport)
  })

  const liveSlide = useMemo(
    () => slides.find(slide => slide.id === liveId) ?? null,
    [slides, liveId],
  )

  // The transport belongs to whatever is live now; a leftover from a superseded push must not
  // drive the monitor. Media items other than video have no transport at all.
  const currentTransport = transport && liveItemId === transport.itemId ? transport : null

  const value = useMemo<LiveController>(
    () => ({
      slides,
      liveId,
      liveSlide,
      origin,
      title,
      advance,
      autoPlaying,
      item,
      liveItemId,
      transport: currentTransport,
      setQueue,
      adoptQueue,
      show,
      step,
      removeSlide,
      clearText,
      clearAll,
      clear: clearAll,
      backdrop,
      setAutoPlaying,
      setSecondsPerSlide,
      playPauseVideo,
      stopVideo,
      seekVideo,
      setVideoLoop,
      setVideoVolume,
      setVideoMuted,
      reportVideoEnded,
      error,
    }),
    [
      slides,
      liveId,
      liveSlide,
      origin,
      title,
      advance,
      autoPlaying,
      item,
      liveItemId,
      currentTransport,
      setQueue,
      adoptQueue,
      show,
      step,
      removeSlide,
      clearText,
      clearAll,
      backdrop,
      setAutoPlaying,
      setSecondsPerSlide,
      playPauseVideo,
      stopVideo,
      seekVideo,
      setVideoLoop,
      setVideoVolume,
      setVideoMuted,
      reportVideoEnded,
      error,
    ],
  )

  return <LiveContext.Provider value={value}>{children}</LiveContext.Provider>
}

export function useLive(): LiveController {
  const controller = useContext(LiveContext)
  if (!controller) throw new Error('useLive must be used inside <LiveProvider>')
  return controller
}
