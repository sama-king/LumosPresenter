import { useCallback, useEffect, useRef, useState } from 'react'

/**
 * Operator console (minimal test surface — real UI comes in a later phase).
 * Shows live transcription and detected references; lets you start/stop
 * listening, switch engine, pick an input device, watch the input level,
 * record the session to a WAV for playback, and type a simulated utterance.
 */

interface Status {
  listening: boolean
  engine: string
  engines: string[]
  deviceId: number | null
  recording: boolean
  lastRecording: string | null
  utteranceWindow: number
  translation: string
}

interface Translation {
  id: string
  name: string
  language: string
  /** 'bundled' / 'easyworship' work offline; 'api.bible' is fetched and cached. */
  source: string
}

interface AudioDevice {
  id: number
  name: string
  maxInputChannels: number
  isDefault: boolean
}

interface TranscriptEvent {
  text: string
  isFinal: boolean
  source: string
  at: string
}

interface ReferenceEvent {
  display: string
  confidence: number
  utterance: string
  translation?: string
  text?: string
}

interface Level {
  peak: number
  rms: number
  clipping: boolean
}

export default function AdminPage() {
  const [status, setStatus] = useState<Status | null>(null)
  const [devices, setDevices] = useState<AudioDevice[]>([])
  const [level, setLevel] = useState<Level>({ peak: 0, rms: 0, clipping: false })
  const [partial, setPartial] = useState('')
  const [transcripts, setTranscripts] = useState<TranscriptEvent[]>([])
  const [references, setReferences] = useState<ReferenceEvent[]>([])
  const [pipelineError, setPipelineError] = useState('')
  const [simulateText, setSimulateText] = useState('')
  const [whisperModels, setWhisperModels] = useState<string[]>([])
  const [selectedModel, setSelectedModel] = useState('')
  const [windowInput, setWindowInput] = useState('')
  const [media, setMedia] = useState<{ id: string; name: string; playbackUrl: string } | null>(null)
  const [mediaBusy, setMediaBusy] = useState(false)
  const [translations, setTranslations] = useState<Translation[]>([])
  // Online translations exist as rows whether or not a key is stored; without one the
  // server refuses them, so this console must not offer them either.
  const [apiKeyConfigured, setApiKeyConfigured] = useState(false)
  const engineRef = useRef<HTMLSelectElement>(null)
  const deviceRef = useRef<HTMLSelectElement>(null)
  const modelRef = useRef<HTMLSelectElement>(null)
  const fileRef = useRef<HTMLInputElement>(null)
  const audioRef = useRef<HTMLAudioElement>(null)

  const refreshStatus = useCallback(() => {
    void fetch('/api/status').then(r => r.json()).then((s: Status) => {
      setStatus(s)
      setWindowInput(prev => (prev === '' ? String(s.utteranceWindow) : prev))
    })
  }, [])

  useEffect(() => {
    refreshStatus()
    void fetch('/api/audio/devices').then(r => r.json()).then(d => setDevices(d.devices))
    void fetch('/api/whisper/models').then(r => r.json()).then(d => {
      setWhisperModels(d.models)
      setSelectedModel(d.selected)
    })
    void fetch('/api/translations').then(r => r.json()).then(d => setTranslations(d.translations))
    void fetch('/api/settings/api-bible')
      .then(r => r.json())
      .then((d: { configured: boolean }) => setApiKeyConfigured(d.configured))
      .catch(() => setApiKeyConfigured(false))

    const events = new EventSource('/events')
    events.addEventListener('transcript', e => {
      const data: TranscriptEvent = JSON.parse((e as MessageEvent).data)
      if (data.isFinal) {
        setPartial('')
        setTranscripts(list => [data, ...list].slice(0, 25))
      } else {
        setPartial(data.text)
      }
    })
    events.addEventListener('reference', e => {
      const data: ReferenceEvent = JSON.parse((e as MessageEvent).data)
      setReferences(list => [data, ...list].slice(0, 25))
    })
    events.addEventListener('status', e => {
      const data = JSON.parse((e as MessageEvent).data)
      setStatus(current => (current ? { ...current, ...data } : current))
      setPipelineError('')
      refreshStatus()
    })
    events.addEventListener('pipelineerror', e => {
      setPipelineError(JSON.parse((e as MessageEvent).data).message)
    })

    // Poll the input level ~10x/sec for the meter.
    const meter = setInterval(() => {
      void fetch('/api/audio/level').then(r => r.json()).then(setLevel).catch(() => {})
    }, 100)

    return () => {
      events.close()
      clearInterval(meter)
    }
  }, [refreshStatus])

  const start = () => void fetch('/api/listening/start', { method: 'POST' }).then(refreshStatus)
  const stop = () => void fetch('/api/listening/stop', { method: 'POST' }).then(refreshStatus)
  const switchEngine = () => {
    const name = engineRef.current?.value
    if (name) void fetch(`/api/engine/${name}`, { method: 'POST' }).then(refreshStatus)
  }
  const selectDevice = () => {
    const raw = deviceRef.current?.value
    const deviceId = raw === '' || raw === undefined ? null : Number(raw)
    void fetch('/api/audio/device', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ deviceId }),
    }).then(async r => {
      if (!r.ok) setPipelineError((await r.json()).message)
      else { setPipelineError(''); refreshStatus() }
    })
  }
  const toggleRecording = () => {
    const on = !status?.recording
    void fetch(`/api/audio/recording/${on}`, { method: 'POST' }).then(refreshStatus)
  }
  const switchTranslation = (code: string) => {
    void fetch(`/api/translation/${code}`, { method: 'POST' }).then(async r => {
      if (!r.ok) setPipelineError((await r.json()).message)
      else { setPipelineError(''); refreshStatus() }
    })
  }
  const setWindow = () => {
    const value = Number(windowInput)
    if (!Number.isFinite(value) || value < 1) return
    void fetch(`/api/parser/window/${Math.round(value)}`, { method: 'POST' })
      .then(r => r.json())
      .then(d => setWindowInput(String(d.utteranceWindow)))
  }
  const switchModel = () => {
    const model = modelRef.current?.value
    if (!model) return
    void fetch('/api/whisper/model', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ model }),
    }).then(async r => {
      if (!r.ok) setPipelineError((await r.json()).message)
      else { setPipelineError(''); setSelectedModel(model) }
    })
  }
  const simulate = (e: React.FormEvent) => {
    e.preventDefault()
    if (!simulateText.trim()) return
    void fetch('/api/simulate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ text: simulateText }),
    })
    setSimulateText('')
  }
  const uploadMedia = () => {
    const file = fileRef.current?.files?.[0]
    if (!file) return
    setMediaBusy(true)
    setPipelineError('')
    setMedia(null)
    const form = new FormData()
    form.append('file', file)
    void fetch('/api/media/upload', { method: 'POST', body: form })
      .then(async r => {
        if (!r.ok) { setPipelineError((await r.json()).message); return }
        setMedia(await r.json())
      })
      .finally(() => setMediaBusy(false))
  }
  const playAndTranscribe = () => {
    if (!media) return
    // Clear prior output, start server transcription, then play the audio in the browser.
    setTranscripts([])
    setReferences([])
    setPartial('')
    void fetch(`/api/media/${media.id}/transcribe`, { method: 'POST' }).then(async r => {
      if (!r.ok) { setPipelineError((await r.json()).message); return }
      const audio = audioRef.current
      if (audio) { audio.currentTime = 0; void audio.play() }
    })
  }
  const transcribeFast = () => {
    if (!media) return
    // No playback — transcribe as fast as the engine allows, for quick testing.
    audioRef.current?.pause()
    setTranscripts([])
    setReferences([])
    setPartial('')
    void fetch(`/api/media/${media.id}/transcribe?fast=true`, { method: 'POST' }).then(async r => {
      if (!r.ok) setPipelineError((await r.json()).message)
    })
  }
  const stopMedia = () => {
    audioRef.current?.pause()
    void fetch('/api/listening/stop', { method: 'POST' })
  }

  const meterWidth = Math.min(100, Math.round(level.rms * 300))
  const peakWidth = Math.min(100, Math.round(level.peak * 100))

  return (
    <main>
      <h1>LumosCast — Operator Console</h1>

      <p>
        Listening: <b>{status ? String(status.listening) : '…'}</b> | Engine: <b>{status?.engine ?? '…'}</b>
        {' '}| Translation:{' '}
        <select value={status?.translation ?? ''} onChange={e => switchTranslation(e.target.value)}>
          {translations
            .filter(t => apiKeyConfigured || t.source !== 'api.bible')
            .map(t => (
              <option key={t.id} value={t.id}>
                {t.id} — {t.name} {t.source === 'api.bible' ? '(online)' : '(offline)'}
              </option>
            ))}
        </select>
      </p>
      {pipelineError && <p>Error: {pipelineError}</p>}

      <p>
        <button onClick={start}>Start listening</button>{' '}
        <button onClick={stop}>Stop</button>{' '}
        <select ref={engineRef} defaultValue={status?.engine}>
          {status?.engines.map(name => (
            <option key={name} value={name}>{name}</option>
          ))}
        </select>{' '}
        <button onClick={switchEngine}>Switch engine</button>
      </p>

      <p>
        Whisper model:{' '}
        <select ref={modelRef} value={selectedModel} onChange={e => setSelectedModel(e.target.value)}>
          {whisperModels.map(m => (
            <option key={m} value={m}>{m}</option>
          ))}
        </select>{' '}
        <button onClick={switchModel}>Load model</button>{' '}
        <small>(only affects the whisper engine; first use of a new model takes a moment to load)</small>
      </p>

      <p>
        Context window (utterances):{' '}
        <input
          type="number"
          min={1}
          max={200}
          value={windowInput}
          onChange={e => setWindowInput(e.target.value)}
          size={4}
          style={{ width: 60 }}
        />{' '}
        <button onClick={setWindow}>Set</button>{' '}
        <small>
          how long a book/chapter stays in context across filler before decaying
          (default 15) — higher tolerates longer pauses between book, chapter, and verse
        </small>
      </p>

      <h2>Audio input (debug)</h2>
      <p>
        Device:{' '}
        <select ref={deviceRef} defaultValue={status?.deviceId ?? ''} disabled={status?.listening}>
          <option value="">(system default)</option>
          {devices.map(d => (
            <option key={d.id} value={d.id}>
              {d.name}{d.isDefault ? ' — default' : ''}
            </option>
          ))}
        </select>{' '}
        <button onClick={selectDevice} disabled={status?.listening}>Use device</button>
        {status?.listening && ' (stop listening to change device)'}
      </p>
      <p>
        Level:{' '}
        <span style={{ display: 'inline-block', width: 300, border: '1px solid #999', height: 14, verticalAlign: 'middle' }}>
          <span style={{
            display: 'inline-block',
            width: `${meterWidth}%`,
            height: '100%',
            background: level.clipping ? 'red' : '#4a4',
          }} />
        </span>{' '}
        peak {peakWidth}% {level.clipping && <b style={{ color: 'red' }}>CLIPPING</b>}
      </p>
      <p>
        <button onClick={toggleRecording}>
          {status?.recording ? 'Recording ON — click to disable' : 'Enable session recording'}
        </button>{' '}
        {status?.lastRecording && (
          <a href="/api/audio/recording/latest">Download last recording ({status.lastRecording})</a>
        )}
        <br />
        <small>
          Enable, then Start listening → speak → Stop. The full session is written to a WAV you can play
          back to confirm the mic signal is clean.
        </small>
      </p>

      <form onSubmit={simulate}>
        <input
          value={simulateText}
          onChange={e => setSimulateText(e.target.value)}
          placeholder="Type an utterance, e.g. john three sixteen"
          size={50}
        />{' '}
        <button type="submit">Simulate</button>
      </form>

      <h2>Transcribe an audio file</h2>
      <p>
        <input ref={fileRef} type="file" accept="audio/*,.mp3,.m4a,.aac,.wav,.aiff,.aif,.caf,.flac,.ogg" />{' '}
        <button onClick={uploadMedia} disabled={mediaBusy}>
          {mediaBusy ? 'Uploading & decoding…' : 'Upload'}
        </button>
        <br />
        <small>audio only (up to 200 MB) — for video, export/extract the audio track first (a 1-hour sermon is ~30–60 MB as MP3/M4A)</small>
      </p>
      {media && (
        <p>
          <b>{media.name}</b><br />
          <audio ref={audioRef} src={media.playbackUrl} controls />
          <br />
          <button onClick={transcribeFast}>Transcribe fast (no playback)</button>{' '}
          <button onClick={playAndTranscribe}>Play + transcribe</button>{' '}
          <button onClick={stopMedia}>Stop</button>{' '}
          <small>runs the file through the current engine — transcript & references fire below, same as the mic. "Fast" skips playback and transcribes as quickly as the engine allows.</small>
        </p>
      )}

      <h2>Detected references</h2>
      <ol>
        {references.map((r, i) => (
          <li key={i}>
            <b>{r.display}</b>{r.translation ? ` (${r.translation})` : ''} (confidence {r.confidence.toFixed(2)}) — “{r.utterance}”
            {r.text && <div><i>{r.text}</i></div>}
          </li>
        ))}
      </ol>

      <h2>Transcript</h2>
      {partial && <p><i>{partial}</i></p>}
      <ol>
        {transcripts.map((t, i) => (
          <li key={i}>
            {t.text} <small>({t.source})</small>
          </li>
        ))}
      </ol>
    </main>
  )
}
