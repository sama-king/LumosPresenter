import { useCallback, useEffect, useRef, useState } from 'react'
import Icon from '../Icon'
import { mediaLibraryFileUrl } from '../../lib/media'
import { useLive, type LiveSlide } from '../../lib/live'
import { useSyncedVideo } from '../../lib/mediaTransport'

/**
 * Whether the operator is listening at the desk. Local to this browser and remembered: the
 * console usually sits in the same room as the speakers, so monitoring is off by default —
 * an operator who turns it on wants it on tomorrow too.
 */
const CUE_KEY = 'lumos:live-monitor-audio'

/**
 * The operator's monitor for the live video, and the transport that drives it everywhere.
 *
 * The video on the displays is not controlled from the displays: play, pause, stop, scrub and
 * loop all happen here and are broadcast, so two screens run one clock instead of two. This
 * element is a follower like any display — it seeks to the same transport — which is what
 * makes it an honest monitor rather than a fourth, separately-drifting copy.
 *
 * It also owns the end-of-clip report that advances a video queue. Displays report too (they
 * are the surfaces that must not stall), and the controller collapses both to one advance.
 *
 * Audio comes in two kinds here, and keeping them apart matters. The fader is PROGRAM level —
 * it travels to the displays and is what the room hears. The headphone button is the operator
 * monitoring at the desk: local to this browser, off by default, and never broadcast, because
 * the console is usually in the same room as the speakers and doubling the audio is worse than
 * not hearing it.
 */
export default function LiveVideoMonitor({ slide }: { slide: Extract<LiveSlide, { kind: 'media' }> }) {
  const live = useLive()
  const { transport, liveItemId } = live
  const videoRef = useRef<HTMLVideoElement>(null)
  const [position, setPosition] = useState(0)
  const [duration, setDuration] = useState(0)
  // Suspends the readout while the operator drags, so the scrubber does not fight the clock.
  const [scrubbing, setScrubbing] = useState<number | null>(null)
  const [cueing, setCueing] = useState(() => localStorage.getItem(CUE_KEY) === '1')

  const toggleCue = useCallback(() => {
    setCueing(previous => {
      const next = !previous
      localStorage.setItem(CUE_KEY, next ? '1' : '0')
      return next
    })
  }, [])

  // The monitor stays silent unless the operator asked to hear it, and follows the program
  // level when they do — so what they hear is what the room is getting.
  useSyncedVideo(videoRef, transport, { silent: !cueing })

  // A new clip is a new clock: drop the previous one's readout rather than letting the
  // scrubber show the last video's length until metadata for this one arrives.
  useEffect(() => {
    setPosition(0)
    setDuration(0)
    setScrubbing(null)
  }, [slide.mediaId, liveItemId])

  const playing = transport?.playing ?? false
  const muted = transport?.muted ?? false
  const volume = transport?.volume ?? 1
  const shown = scrubbing ?? position

  return (
    <div className="flex flex-col gap-2">
      <div className="live-glow overflow-hidden rounded border border-emerald-live/50 bg-black">
        <video
          ref={videoRef}
          key={`${liveItemId ?? 'none'}:${slide.mediaId}`}
          src={mediaLibraryFileUrl(slide.mediaId)}
          muted
          playsInline
          preload="auto"
          onLoadedMetadata={e => setDuration(e.currentTarget.duration)}
          onTimeUpdate={e => setPosition(e.currentTarget.currentTime)}
          onEnded={() => liveItemId && live.reportVideoEnded(liveItemId)}
          className="h-32 w-full object-contain"
        />
      </div>

      <input
        type="range"
        min={0}
        max={Math.max(0.1, duration)}
        step={0.1}
        value={Math.min(shown, Math.max(0.1, duration))}
        disabled={duration === 0}
        aria-label="Seek"
        onChange={e => setScrubbing(Number(e.target.value))}
        onPointerUp={() => {
          if (scrubbing !== null) live.seekVideo(scrubbing)
          setScrubbing(null)
        }}
        onKeyUp={() => {
          if (scrubbing !== null) live.seekVideo(scrubbing)
          setScrubbing(null)
        }}
        className="h-1 w-full cursor-pointer appearance-none rounded-full bg-surface-container-highest accent-emerald-live disabled:opacity-40"
      />

      <div className="flex items-center gap-1.5">
        <button
          type="button"
          onClick={live.playPauseVideo}
          title={playing ? 'Pause on all displays' : 'Play on all displays'}
          className="rounded border border-emerald-live/50 bg-emerald-live/10 p-1.5 text-emerald-live transition-colors hover:bg-emerald-live/20"
        >
          <Icon name={playing ? 'pause' : 'play_arrow'} size={18} />
        </button>
        <button
          type="button"
          onClick={live.stopVideo}
          title="Stop and rewind on all displays"
          className="rounded border border-outline-variant bg-surface-container p-1.5 text-on-surface-variant transition-colors hover:text-on-surface"
        >
          <Icon name="stop" size={18} />
        </button>
        <button
          type="button"
          onClick={() => live.setVideoLoop(!(transport?.loop ?? false))}
          title={transport?.loop ? 'Playing on repeat' : 'Playing once'}
          className={`rounded border p-1.5 transition-colors ${
            transport?.loop
              ? 'border-emerald-live/50 bg-emerald-live/10 text-emerald-live'
              : 'border-outline-variant bg-surface-container text-on-surface-variant hover:text-on-surface'
          }`}
        >
          <Icon name="repeat" size={18} />
        </button>
        <span className="ml-auto font-mono text-mono-ui tabular-nums text-slate-muted">
          {clock(shown)} / {duration > 0 ? clock(duration) : '--:--'}
        </span>
      </div>

      <div className="flex items-center gap-1.5">
        <button
          type="button"
          onClick={() => live.setVideoMuted(!muted)}
          title={muted ? 'Sound is muted on the displays' : 'Mute sound on the displays'}
          className={`rounded border p-1.5 transition-colors ${
            muted
              ? 'border-rose-error/50 bg-rose-error/10 text-rose-error'
              : 'border-outline-variant bg-surface-container text-on-surface-variant hover:text-on-surface'
          }`}
        >
          <Icon name={muted ? 'volume_off' : 'volume_up'} size={18} />
        </button>
        <input
          type="range"
          min={0}
          max={1}
          step={0.01}
          value={muted ? 0 : volume}
          aria-label="Volume on the displays"
          onChange={e => live.setVideoVolume(Number(e.target.value))}
          className="h-1 flex-1 cursor-pointer appearance-none rounded-full bg-surface-container-highest accent-emerald-live"
        />
        <button
          type="button"
          onClick={toggleCue}
          title={
            cueing
              ? 'Listening here as well as on the displays — click to silence this monitor'
              : 'Listen to the clip here (this console only — the displays are unaffected)'
          }
          className={`rounded border p-1.5 transition-colors ${
            cueing
              ? 'border-primary/50 bg-primary/10 text-primary'
              : 'border-outline-variant bg-surface-container text-on-surface-variant hover:text-on-surface'
          }`}
        >
          <Icon name="headphones" size={18} />
        </button>
      </div>
    </div>
  )
}

/** m:ss, which is all a service clip ever needs. */
function clock(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) return '0:00'
  const whole = Math.floor(seconds)
  return `${Math.floor(whole / 60)}:${String(whole % 60).padStart(2, '0')}`
}
