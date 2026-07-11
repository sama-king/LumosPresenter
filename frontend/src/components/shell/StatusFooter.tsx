import { useEffect, useRef, useState } from 'react'
import { api } from '../../lib/api'
import { useServerEvent } from '../../lib/events'
import type { AudioLevel, StatusDto, TranscriptEvent } from '../../lib/types'
import Icon from '../Icon'

/**
 * App-wide status footer, ported from stitch_designs/stage_configuration
 * (the authoritative chrome for all pages): auto-detection toggle, live
 * waveform, transcript preview, and engine/model picker. LAT/CPU render as
 * placeholders until the backend exposes metrics.
 *
 * Listening state is server-authoritative: seeded from /api/status, then only
 * the `status` SSE event and POST responses update it — no optimistic flip, so
 * the toggle can't fight a stale mount fetch (the flicker bug).
 */

const BAR_COUNT = 48

function barColor(height: number): string {
  if (height > 78) return 'var(--color-rose-error)'
  if (height > 55) return 'var(--color-primary-container)'
  return 'var(--color-emerald-live)'
}

/** "ggml-small.en.bin" → "small.en" for a compact label. */
function shortModelName(file: string): string {
  return file.replace(/^ggml-/, '').replace(/\.bin$/, '')
}

function Waveform({ listening }: { listening: boolean }) {
  const [bars, setBars] = useState<number[]>(() => Array(BAR_COUNT).fill(4))

  useEffect(() => {
    if (!listening) {
      setBars(Array(BAR_COUNT).fill(4))
      return
    }
    const timer = setInterval(() => {
      void api
        .getAudioLevel()
        .then((level: AudioLevel) => {
          const height = Math.max(4, Math.min(100, Math.round(level.rms * 300)))
          setBars(prev => [...prev.slice(1), height])
        })
        .catch(() => {})
    }, 150)
    return () => clearInterval(timer)
  }, [listening])

  return (
    <div className="flex h-6 w-48 items-end gap-[2px] overflow-hidden">
      {bars.map((height, i) => (
        <div
          key={i}
          className="w-[3px] rounded-full transition-all duration-150"
          style={{ height: `${height}%`, backgroundColor: barColor(height) }}
        />
      ))}
    </div>
  )
}

function ModelPicker({
  engine,
  onError,
}: {
  engine: string
  onError: (message: string) => void
}) {
  const [models, setModels] = useState<string[]>([])
  const [selected, setSelected] = useState('')
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    void api
      .getWhisperModels()
      .then(data => {
        setModels(data.models)
        setSelected(data.selected)
      })
      .catch(() => {})
  }, [])

  // The status SSE only carries the provider name; the model comes from here.
  const isWhisper = engine === 'whisper'

  const pick = (model: string) => {
    setOpen(false)
    if (model === selected || busy) return
    setBusy(true)
    void api
      .setWhisperModel(model)
      .then(data => setSelected(data.selected))
      .catch((err: Error) => onError(err.message))
      .finally(() => setBusy(false))
  }

  return (
    <div className="relative flex flex-col items-end">
      <span className="font-mono text-[9px] uppercase text-slate-muted">Engine</span>
      <button
        type="button"
        disabled={!isWhisper || busy}
        onClick={() => setOpen(o => !o)}
        title={isWhisper ? 'Change Whisper model' : 'Sherpa-onnx (single model)'}
        className="flex items-center gap-1 text-[11px] font-bold text-on-surface transition-colors hover:text-primary disabled:cursor-default disabled:hover:text-on-surface"
      >
        {isWhisper ? `Whisper · ${selected ? shortModelName(selected) : '…'}` : 'sherpa-onnx'}
        {isWhisper && <Icon name={open ? 'expand_less' : 'expand_more'} size={16} />}
      </button>
      {open && isWhisper && (
        <ul className="absolute bottom-full right-0 z-50 mb-2 max-h-64 w-56 overflow-y-auto rounded border border-outline-variant bg-surface-container-high py-1 shadow-xl">
          {models.map(model => (
            <li key={model}>
              <button
                type="button"
                onClick={() => pick(model)}
                className={`flex w-full items-center justify-between px-3 py-1.5 text-left font-mono text-mono-ui transition-colors hover:bg-surface-container-highest ${
                  model === selected ? 'text-primary' : 'text-on-surface-variant'
                }`}
              >
                {shortModelName(model)}
                {model === selected && <Icon name="check" size={14} />}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function InputPicker({
  listening,
  onError,
}: {
  listening: boolean
  onError: (message: string) => void
}) {
  const [devices, setDevices] = useState<{ id: number; name: string; isDefault: boolean }[]>([])
  const [selected, setSelected] = useState<number | null>(null)
  const [open, setOpen] = useState(false)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    void api
      .getAudioDevices()
      .then(data => {
        setDevices(data.devices)
        setSelected(data.selected)
      })
      .catch(() => {})
  }, [])

  const pick = (deviceId: number | null) => {
    setOpen(false)
    if (deviceId === selected || busy) return
    setBusy(true)
    void api
      .setAudioDevice(deviceId)
      .then(() => setSelected(deviceId))
      .catch((err: Error) => onError(err.message))
      .finally(() => setBusy(false))
  }

  const label =
    selected === null
      ? 'System default'
      : (devices.find(d => d.id === selected)?.name ?? `Device ${selected}`)

  return (
    <div className="relative flex flex-col">
      <span className="font-mono text-[9px] uppercase text-slate-muted">Input</span>
      <button
        type="button"
        disabled={listening || busy}
        onClick={() => setOpen(o => !o)}
        title={listening ? 'Stop auto-detection to change input' : 'Change audio input'}
        className="flex max-w-40 items-center gap-1 text-[11px] font-bold text-on-surface transition-colors hover:text-primary disabled:cursor-not-allowed disabled:text-slate-muted disabled:hover:text-slate-muted"
      >
        <Icon name="mic" size={14} className="shrink-0" />
        <span className="truncate">{label}</span>
        {!listening && <Icon name={open ? 'expand_less' : 'expand_more'} size={16} />}
      </button>
      {open && !listening && (
        <ul className="absolute bottom-full left-0 z-50 mb-2 max-h-64 w-64 overflow-y-auto rounded border border-outline-variant bg-surface-container-high py-1 shadow-xl">
          <li>
            <button
              type="button"
              onClick={() => pick(null)}
              className={`flex w-full items-center justify-between px-3 py-1.5 text-left font-mono text-mono-ui transition-colors hover:bg-surface-container-highest ${
                selected === null ? 'text-primary' : 'text-on-surface-variant'
              }`}
            >
              System default
              {selected === null && <Icon name="check" size={14} />}
            </button>
          </li>
          {devices.map(device => (
            <li key={device.id}>
              <button
                type="button"
                onClick={() => pick(device.id)}
                className={`flex w-full items-center justify-between gap-2 px-3 py-1.5 text-left font-mono text-mono-ui transition-colors hover:bg-surface-container-highest ${
                  device.id === selected ? 'text-primary' : 'text-on-surface-variant'
                }`}
              >
                <span className="truncate">
                  {device.name}
                  {device.isDefault && <span className="text-slate-muted"> · default</span>}
                </span>
                {device.id === selected && <Icon name="check" size={14} className="shrink-0" />}
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

export default function StatusFooter() {
  const [listening, setListening] = useState(false)
  const [engine, setEngine] = useState('')
  const [translation, setTranslation] = useState('')
  const [transcript, setTranscript] = useState('')
  const [error, setError] = useState('')
  const seeded = useRef(false)

  useEffect(() => {
    void api
      .getStatus()
      .then((s: StatusDto) => {
        // Seed once; after this only the SSE/POST responses are authoritative,
        // so a slow mount fetch can never clobber a newer toggle.
        seeded.current = true
        setListening(s.listening)
        setEngine(s.engine)
        setTranslation(s.translation)
      })
      .catch(() => {
        seeded.current = true
      })
  }, [])

  useServerEvent<{ listening: boolean; engine: string }>('status', data => {
    setListening(data.listening)
    setEngine(data.engine)
  })
  useServerEvent<{ translation: string }>('translation', data => setTranslation(data.translation))
  useServerEvent<TranscriptEvent>('transcript', data => setTranscript(data.text))

  const showError = (message: string) => {
    setError(message)
    window.setTimeout(() => setError(''), 6000)
  }

  // Fire the request and let the resulting `status` SSE event flip the UI — no
  // optimistic local flip to race against.
  const toggleListening = () => {
    void (listening ? api.stopListening() : api.startListening()).catch((err: Error) =>
      showError(err.message),
    )
  }

  return (
    <footer
      data-theme="dark"
      className="z-50 flex h-20 shrink-0 items-center justify-between border-t border-outline-variant bg-surface px-gutter"
    >
      <div className="flex items-center gap-10">
        <div
          className={`flex items-center gap-4 rounded-full border bg-app-bg/50 px-4 py-2 ${
            listening ? 'border-emerald-live/20' : 'border-outline-variant/50'
          }`}
        >
          <div className="flex flex-col">
            <span className="font-mono text-[9px] uppercase text-slate-muted">Auto-detection</span>
            <div className="flex items-center gap-2">
              <span
                className={`text-[11px] font-bold ${listening ? 'text-emerald-live' : 'text-slate-muted'}`}
              >
                {listening ? 'ACTIVE' : 'OFF'}
              </span>
              <button
                type="button"
                onClick={toggleListening}
                title={listening ? 'Stop auto-detection' : 'Start auto-detection'}
                className={`relative h-4 w-8 rounded-full border transition-all ${
                  listening
                    ? 'border-emerald-live/50 bg-emerald-live/30'
                    : 'border-outline-variant bg-surface-container-highest'
                }`}
              >
                <div
                  className={`absolute top-0.5 h-3 w-3 rounded-full transition-all ${
                    listening ? 'right-0.5 bg-emerald-live' : 'left-0.5 bg-slate-muted'
                  }`}
                />
              </button>
            </div>
          </div>
          <div className="h-8 w-px bg-outline-variant/30" />
          <InputPicker listening={listening} onError={showError} />
          <div className="h-8 w-px bg-outline-variant/30" />
          <Waveform listening={listening} />
        </div>

        {/* Live transcript preview (1–2 lines) */}
        <div className="flex max-w-md flex-col">
          <span className="font-mono text-[9px] uppercase text-slate-muted">Transcript</span>
          <span className="line-clamp-2 min-h-4 font-mono text-mono-ui text-on-surface-variant">
            {transcript || '—'}
          </span>
        </div>

        {error && (
          <span className="font-mono text-mono-ui text-rose-error" role="alert">
            {error}
          </span>
        )}
      </div>

      <div className="flex items-center gap-6">
        <ModelPicker engine={engine} onError={showError} />
        <div className="flex items-center gap-4 rounded-lg border border-outline-variant bg-surface-container px-3 py-1.5">
          <div className="flex items-center gap-2" title="CPU (coming soon)">
            <Icon name="memory" size={16} className="text-amber-warning" />
            <span className="font-mono text-[11px]">—</span>
          </div>
          <div className="flex items-center gap-2" title="Active translation">
            <Icon name="database" size={16} className="text-primary" />
            <span className="font-mono text-[11px]">{translation || '—'}</span>
          </div>
        </div>
      </div>
    </footer>
  )
}
