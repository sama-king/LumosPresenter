import { useEffect, useRef } from 'react'
import type { MediaTransportDto } from './types'

/**
 * Following the console's video clock.
 *
 * A projection display holds its own <video> element — nothing can change that — but it must
 * not run its own playback. Left to autoplay, two screens start whenever their file happens to
 * decode and drift apart from there. Instead the console owns one clock and every surface,
 * including the console's own monitor, seeks to it.
 *
 * The anchor below is that clock as this browser sees it. It deliberately measures from LOCAL
 * receipt time rather than the server's timestamp: displays can be on other machines whose
 * wall clocks disagree by seconds, and a seek is only ever as good as the clock it is measured
 * against. What the server's timestamp would buy (accounting for network latency) is worth far
 * less than what clock skew would cost.
 */
export interface TransportAnchor {
  itemId: string
  mediaId: string
  playing: boolean
  /** Clip position, in seconds, at `anchoredAt`. */
  position: number
  loop: boolean
  /** Program audio: what the room hears, set from the console. */
  volume: number
  muted: boolean
  revision: number
  /** performance.now() when this anchor was taken. */
  anchoredAt: number
  /**
   * Bumped by every heartbeat that re-states the same revision. The anchor itself must NOT
   * move then — re-anchoring on a beat would pin the clock to the last seek and stop it
   * advancing — but a changed tick re-runs the drift check, which is the point of the beat.
   */
  tick: number
}

/** Drift a display tolerates before it seeks. Below this a correction is more visible than the error. */
const DRIFT_TOLERANCE_SECONDS = 0.45

/**
 * Folds a transport update into the anchor. A repeat of a revision already anchored keeps the
 * original timing and only bumps the tick, so the clock keeps running while the heartbeat
 * still triggers a drift check.
 */
export function anchorTransport(
  previous: TransportAnchor | null,
  dto: MediaTransportDto,
): TransportAnchor {
  if (previous && previous.revision === dto.revision && previous.itemId === dto.itemId) {
    return { ...previous, tick: previous.tick + 1 }
  }
  return {
    itemId: dto.itemId,
    mediaId: dto.mediaId,
    playing: dto.playing,
    position: dto.position,
    loop: dto.loop,
    volume: dto.volume,
    muted: dto.muted,
    revision: dto.revision,
    anchoredAt: performance.now(),
    tick: 0,
  }
}

/** Where the clip should be right now, in seconds — unwrapped, so it can exceed the duration. */
export function transportPosition(anchor: TransportAnchor, now = performance.now()): number {
  if (!anchor.playing) return anchor.position
  return anchor.position + Math.max(0, now - anchor.anchoredAt) / 1000
}

/** Where the clip should be, folded back into [0, duration) for a looping clip. */
export function wrappedPosition(anchor: TransportAnchor, duration: number): number {
  const raw = transportPosition(anchor)
  if (!anchor.loop || !Number.isFinite(duration) || duration <= 0) return raw
  return raw % duration
}

/**
 * Keeps a <video> element on the console's clock: seeks when it has drifted, plays or pauses
 * to match, sets its audio, and re-checks on every heartbeat.
 *
 * Pass null when no video is live. The element is left alone then — it is about to unmount.
 *
 * `silent` forces this element mute regardless of the transport. It is how a surface says the
 * sound is not its job: a display the operator has not designated for audio, or the console's
 * own monitor, which stays quiet unless the operator asks to hear it at the desk.
 */
export function useSyncedVideo(
  videoRef: React.RefObject<HTMLVideoElement | null>,
  anchor: TransportAnchor | null,
  options?: { silent?: boolean; volume?: number },
) {
  const silent = options?.silent ?? false
  const localVolume = options?.volume
  // The effect below depends on the anchor's fields, not its identity, so a re-render that
  // produced an equal anchor does not re-seek.
  const key = anchor
    ? [
        anchor.itemId,
        anchor.revision,
        anchor.tick,
        String(anchor.playing),
        String(anchor.loop),
        String(silent),
        localVolume ?? '',
      ].join(':')
    : ''
  const anchorRef = useRef(anchor)
  anchorRef.current = anchor

  useEffect(() => {
    const video = videoRef.current
    const current = anchorRef.current
    if (!video || !current) return

    const apply = () => {
      video.loop = current.loop
      video.muted = silent || current.muted
      video.volume = Math.min(1, Math.max(0, localVolume ?? current.volume))
      const target = wrappedPosition(current, video.duration)
      if (Math.abs(video.currentTime - target) > DRIFT_TOLERANCE_SECONDS) {
        video.currentTime = target
      }
      if (current.playing) {
        void video.play().catch(() => {
          // Browsers only autoplay muted media until the window has been interacted with, so
          // an unmuted clip on a display nobody has clicked is refused. Falling back to muted
          // keeps the picture running: a silent video is a far smaller failure on a projector
          // than a frozen frame, and the next transport event retries with sound.
          if (video.muted) return
          video.muted = true
          void video.play().catch(() => {})
        })
      } else {
        video.pause()
      }
    }

    // Seeking before metadata lands throws away the seek, so wait for a duration first.
    if (video.readyState >= 1) {
      apply()
      return
    }
    video.addEventListener('loadedmetadata', apply, { once: true })
    return () => video.removeEventListener('loadedmetadata', apply)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [key, videoRef, silent, localVolume])
}
