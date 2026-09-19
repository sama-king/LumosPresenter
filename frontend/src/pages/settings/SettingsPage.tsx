import { useCallback, useEffect, useRef, useState } from 'react'
import Icon from '../../components/Icon'
import { api } from '../../lib/api'
import { useServerEvent } from '../../lib/events'
import type {
  ApiBibleKeyStatus,
  AudioDevice,
  AudioLevel,
  StatusDto,
  Translation,
} from '../../lib/types'
import { isOfflineTranslation } from '../../lib/types'
import SettingsSection from './SettingsSection'
import Slider from './Slider'

/**
 * Console settings, ported from stitch_designs/refined_settings_console.
 *
 * The design splits its content across three tabs; this is one scrolling page with
 * labelled sections instead, so nothing is hidden behind a control the operator has to
 * discover mid-service. Sections that the design invents without anything behind them
 * (profile, licence, update checks, backups) are left out rather than built as dead
 * chrome — every control here is wired to real server state.
 *
 * Nothing is staged: each control applies on change, matching the rest of the console
 * (engine switch, translation switch). There is deliberately no Save/Discard bar — a
 * settings page that needs saving is a settings page that can be left half-applied while
 * a service is running.
 */

/** What each engine is actually good at — the operator's basis for choosing. */
const ENGINE_NOTES: Record<string, { title: string; blurb: string; trait: string; icon: string }> = {
  whisper: {
    title: 'Whisper.net',
    blurb: 'High accuracy on scripture and liturgical language. The default.',
    trait: 'Accuracy',
    icon: 'verified',
  },
  'sherpa-onnx': {
    title: 'sherpa-onnx',
    blurb: 'Ultra-low latency for fast, conversational preaching.',
    trait: 'Speed',
    icon: 'bolt',
  },
}

/** Matches TranscriptionPipeline.UtteranceWindow's own clamp. */
const WINDOW_MAX = 200
/** Thousandths of RMS — the server clamps the speech threshold to 0.001–0.1. */
const VAD_MAX = 100

/** "ggml-small.en.bin" → "small.en", as the status footer labels it. */
function shortModelName(file: string): string {
  return file.replace(/^ggml-/, '').replace(/\.bin$/, '')
}

export default function SettingsPage() {
  const [status, setStatus] = useState<StatusDto | null>(null)
  const [devices, setDevices] = useState<AudioDevice[]>([])
  const [models, setModels] = useState<string[]>([])
  const [selectedModel, setSelectedModel] = useState('')
  const [translations, setTranslations] = useState<Translation[]>([])
  const [apiKey, setApiKey] = useState<ApiBibleKeyStatus | null>(null)
  const [message, setMessage] = useState<{ kind: 'error' | 'ok'; text: string } | null>(null)

  // Slider positions are local so dragging stays smooth; the server is told on release.
  const [window_, setWindow_] = useState(15)
  const [confidence, setConfidence] = useState(75)
  const [vadThreshold, setVadThreshold] = useState(30)

  // Live input level, pushed on the shared event stream, so the operator can pick a
  // threshold by looking at their actual room rather than guessing a number.
  const [level, setLevel] = useState<AudioLevel>({ peak: 0, rms: 0, clipping: false })
  useServerEvent<AudioLevel>('level', setLevel)

  // Listening is toggled from the footer, which is on screen while this page is open, so
  // the mount fetch alone would leave the level hint claiming nothing is listening. Track
  // the same server-authoritative status event the footer does; it arrives as a patch.
  useServerEvent<Partial<StatusDto>>('status', patch =>
    setStatus(current => (current ? { ...current, ...patch } : current)),
  )

  const [keyInput, setKeyInput] = useState('')
  const [editingKey, setEditingKey] = useState(false)
  const [savingKey, setSavingKey] = useState(false)

  const messageTimer = useRef<number | undefined>(undefined)
  const notify = useCallback((kind: 'error' | 'ok', text: string) => {
    setMessage({ kind, text })
    globalThis.clearTimeout(messageTimer.current)
    messageTimer.current = globalThis.setTimeout(() => setMessage(null), 5000)
  }, [])
  const fail = useCallback((err: Error) => notify('error', err.message), [notify])

  const loadStatus = useCallback(
    () =>
      api
        .getStatus()
        .then(s => {
          setStatus(s)
          setWindow_(s.utteranceWindow)
          setConfidence(Math.round(s.autoLiveConfidence * 100))
          setVadThreshold(Math.round(s.vadThreshold * 1000))
        })
        .catch(fail),
    [fail],
  )

  useEffect(() => {
    void loadStatus()
    void api.getAudioDevices().then(d => setDevices(d.devices)).catch(fail)
    void api
      .getWhisperModels()
      .then(d => {
        setModels(d.models)
        setSelectedModel(d.selected)
      })
      .catch(fail)
    void api.getTranslations().then(d => setTranslations(d.translations)).catch(fail)
    void api
      .getApiBibleKey()
      .then(setApiKey)
      .catch(() => setApiKey({ configured: false, hint: null }))
    return () => globalThis.clearTimeout(messageTimer.current)
  }, [loadStatus, fail])

  const switchEngine = (name: string) => {
    if (name === status?.engine) {
      return
    }
    void api
      .setEngine(name)
      .then(() => loadStatus())
      .then(() => notify('ok', `Switched to ${ENGINE_NOTES[name]?.title ?? name}.`))
      .catch(fail)
  }

  const switchModel = (model: string) => {
    setSelectedModel(model)
    void api
      .setWhisperModel(model)
      .then(() => notify('ok', `Loading ${shortModelName(model)} — first use takes a moment.`))
      .catch(fail)
  }

  const selectDevice = (raw: string) => {
    const deviceId = raw === '' ? null : Number(raw)
    void api
      .setAudioDevice(deviceId)
      .then(() => loadStatus())
      .catch(fail)
  }

  const saveKey = () => {
    const value = keyInput.trim()
    if (!value || savingKey) {
      return
    }
    setSavingKey(true)
    void api
      .setApiBibleKey(value)
      .then(s => {
        setApiKey(s)
        setKeyInput('')
        setEditingKey(false)
        notify('ok', 'Key saved. Online Bibles are available now.')
      })
      .catch(fail)
      .finally(() => setSavingKey(false))
  }

  const removeKey = () => {
    setSavingKey(true)
    void api
      .clearApiBibleKey()
      .then(s => {
        setApiKey(s)
        setKeyInput('')
        setEditingKey(false)
        notify('ok', 'Key removed. Online Bibles are switched off.')
      })
      .catch(fail)
      .finally(() => setSavingKey(false))
  }

  const engines = status?.engines ?? []
  // Whisper.net names its runtimes "Vulkan", "Cpu", "CoreML"…; compare case-insensitively
  // rather than depending on that casing.
  const accelerator = status?.accelerator?.toLowerCase() ?? null
  const online = translations.filter(t => !isOfflineTranslation(t))
  const offline = translations.filter(isOfflineTranslation)
  const hasKey = apiKey?.configured === true

  return (
    <div className="panel-scroll min-h-0 flex-1 overflow-y-auto">
      <div className="mx-auto flex max-w-5xl flex-col gap-12 px-gutter py-8">
        <header className="flex items-baseline justify-between">
          <div>
            <h1 className="font-display text-headline-lg text-on-surface">Settings</h1>
            <p className="mt-1 text-body-md text-on-surface-variant">
              Changes apply immediately — there is nothing to save.
            </p>
          </div>
          {message && (
            <p
              role="status"
              className={`font-mono text-mono-ui ${
                message.kind === 'error' ? 'text-rose-error' : 'text-emerald-live'
              }`}
            >
              {message.text}
            </p>
          )}
        </header>

        {/* --- Transcription ------------------------------------------------ */}
        <SettingsSection
          title="Speech Engine"
          caption="Backend"
          description="Which engine turns the microphone into text. Switching restarts the pipeline."
        >
          <div className="flex flex-col gap-3">
            {engines.map(name => {
              const note = ENGINE_NOTES[name]
              const active = status?.engine === name
              return (
                <button
                  key={name}
                  type="button"
                  onClick={() => switchEngine(name)}
                  aria-pressed={active}
                  className={`flex items-center gap-4 rounded-xl border p-4 text-left transition-all ${
                    active
                      ? 'border-primary bg-slate-surface shadow-[0_0_15px_rgba(173,198,255,0.25)]'
                      : 'border-outline-variant bg-surface-container hover:border-primary-container'
                  }`}
                >
                  <div className="min-w-0 flex-1">
                    <div className="mb-1 flex items-center gap-3">
                      <p className="font-bold text-on-surface">{note?.title ?? name}</p>
                      {name === 'whisper' && (
                        <span className="rounded bg-primary/20 px-2 py-0.5 text-[10px] font-bold uppercase text-primary">
                          Recommended
                        </span>
                      )}
                    </div>
                    <p className="text-body-md leading-relaxed text-on-surface-variant">
                      {note?.blurb ?? 'Alternative transcription backend.'}
                    </p>
                  </div>
                  {note && (
                    <span
                      className={`flex shrink-0 items-center gap-2 font-mono text-mono-ui uppercase ${
                        note.trait === 'Accuracy' ? 'text-emerald-live' : 'text-secondary'
                      }`}
                    >
                      <Icon name={note.icon} size={16} />
                      {note.trait}
                    </span>
                  )}
                </button>
              )
            })}
          </div>

          {/* Only whisper has selectable weights, so this rides with the engine cards. */}
          <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-outline-variant bg-surface-container p-5">
            <div>
              <p className="font-bold text-on-surface">Whisper model</p>
              <p className="text-body-md text-on-surface-variant">
                Larger models read harder audio but load slower. Only affects Whisper.net.
              </p>
            </div>
            <select
              value={selectedModel}
              onChange={e => switchModel(e.target.value)}
              className="rounded border border-outline-variant bg-surface-container-lowest px-3 py-2 font-mono text-mono-ui text-on-surface focus:border-primary focus:outline-none"
            >
              {models.map(m => (
                <option key={m} value={m}>
                  {shortModelName(m)}
                </option>
              ))}
            </select>
          </div>

          {/*
            Windows only, and deliberately so: macOS gets CoreML acceleration automatically
            and has no Vulkan path, so the row would be noise there. The platform comes from
            the server — the console may be running on a different machine than the engine.
          */}
          {status?.platform === 'windows' && (
            <div className="mt-4 flex flex-wrap items-center justify-between gap-3 rounded-xl border border-outline-variant bg-surface-container p-5">
              <div>
                <p className="font-bold text-on-surface">GPU acceleration</p>
                <p className="text-body-md text-on-surface-variant">
                  {accelerator === null
                    ? 'Detected when the engine first loads a model — start listening once to find out.'
                    : accelerator === 'vulkan'
                      ? 'Vulkan is running on your graphics chip, roughly halving transcription time.'
                      : 'Running on the CPU. Transcription may not keep up with live speech on modest hardware.'}
                </p>
              </div>
              <span
                className={`flex shrink-0 items-center gap-2 rounded-full border px-4 py-2 font-mono text-mono-ui uppercase ${
                  accelerator === 'vulkan'
                    ? 'border-emerald-live/30 text-emerald-live'
                    : accelerator === null
                      ? 'border-outline-variant text-slate-muted'
                      : 'border-amber-warning/30 text-amber-warning'
                }`}
              >
                <Icon
                  name={
                    accelerator === 'vulkan'
                      ? 'bolt'
                      : accelerator === null
                        ? 'help'
                        : 'memory'
                  }
                  size={16}
                />
                {accelerator === null ? 'Unknown' : accelerator === 'vulkan' ? 'Vulkan' : 'CPU only'}
              </span>
            </div>
          )}
        </SettingsSection>

        <SettingsSection
          title="Reference Detection"
          caption="Parser"
          description="How the parser turns speech into verse references, and when it acts on its own."
        >
          <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
            <div className="rounded-xl border border-outline-variant bg-surface-container p-6">
              <div className="mb-6 flex items-start justify-between gap-4">
                <div>
                  <h3 className="text-body-lg font-bold text-on-surface">Context window</h3>
                  <p className="text-body-md text-on-surface-variant">
                    How long a spoken book and chapter stay in context while the speaker
                    pauses or fills, before the reference decays.
                  </p>
                </div>
                <div className="shrink-0 rounded border border-outline-variant bg-navy-deep px-3 py-1">
                  <span className="font-display text-headline-md text-primary">{window_}</span>
                  <span className="ml-1 font-mono text-[10px] uppercase text-slate-muted">
                    Utterances
                  </span>
                </div>
              </div>
              <Slider
                // The server clamps to 1–200; the slider must span the same range or an
                // existing higher setting would be silently rewritten on first touch.
                min={1}
                max={WINDOW_MAX}
                value={window_}
                onChange={setWindow_}
                onCommit={v =>
                  void api
                    .setParserWindow(v)
                    .then(r => setWindow_(r.utteranceWindow))
                    .catch(fail)
                }
                lowLabel="Short (snappy)"
                highLabel="Long (tolerates pauses)"
                ariaLabel="Context window in utterances"
              />
            </div>

            <div className="rounded-xl border border-outline-variant bg-surface-container-high p-6">
              <div className="mb-6 flex items-start justify-between gap-4">
                <div>
                  <h3 className="text-body-lg font-bold text-on-surface">Auto-live threshold</h3>
                  <p className="text-body-md text-on-surface-variant">
                    How sure the parser must be before it pushes a detected verse to the
                    displays without an operator.
                  </p>
                </div>
                <span className="shrink-0 font-display text-headline-lg text-primary drop-shadow-[0_0_10px_rgba(173,198,255,0.4)]">
                  {confidence}%
                </span>
              </div>
              <Slider
                min={50}
                max={100}
                value={confidence}
                onChange={setConfidence}
                onCommit={v =>
                  void api
                    .setAutoLiveConfidence(v)
                    .then(r => setConfidence(Math.round(r.autoLiveConfidence * 100)))
                    .catch(fail)
                }
                lowLabel="Relaxed"
                highLabel="Strict"
                ariaLabel="Auto-live confidence threshold"
              />
              <div className="mt-6 flex gap-3 rounded-lg border border-outline-variant/30 bg-navy-deep/50 p-4">
                <Icon name="info" size={20} className="text-amber-warning" />
                <p className="text-mono-ui italic leading-tight text-on-surface-variant">
                  A high threshold reduces false pushes but may leave quieter speakers
                  waiting for the operator.
                </p>
              </div>
            </div>
          </div>
        </SettingsSection>

        {/* --- Bibles ------------------------------------------------------- */}
        <SettingsSection
          title="Online Bible Sources"
          caption="api.bible"
          description="NIV, AMP and MSG are fetched from api.bible and cached locally. They need your own key — it is stored on this machine and never shared."
        >
          <div className="rounded-xl border border-outline-variant bg-surface-container p-6">
            <div className="flex flex-wrap items-center justify-between gap-4">
              <div className="flex items-center gap-3">
                <span
                  className={`h-2 w-2 rounded-full ${
                    hasKey ? 'bg-emerald-live shadow-[0_0_5px_#10B981]' : 'bg-slate-muted'
                  }`}
                />
                <div>
                  <p className="font-bold text-on-surface">
                    {hasKey ? 'Key configured' : 'No key set'}
                  </p>
                  <p className="font-mono text-mono-ui text-slate-muted">
                    {hasKey
                      ? `Stored key ending ${apiKey?.hint ?? ''} — online Bibles are available.`
                      : 'Online Bibles stay unavailable until a key is saved.'}
                  </p>
                </div>
              </div>
              {hasKey && !editingKey && (
                <span className="flex gap-2">
                  <button
                    type="button"
                    onClick={() => setEditingKey(true)}
                    className="rounded border border-outline-variant bg-surface-container-highest px-4 py-2 font-mono text-mono-ui uppercase text-on-surface transition-colors hover:border-primary/40"
                  >
                    Change
                  </button>
                  <button
                    type="button"
                    onClick={removeKey}
                    disabled={savingKey}
                    className="rounded border border-outline-variant px-4 py-2 font-mono text-mono-ui uppercase text-slate-muted transition-colors hover:border-rose-error/50 hover:text-rose-error disabled:opacity-40"
                  >
                    Remove
                  </button>
                </span>
              )}
            </div>

            {(!hasKey || editingKey) && (
              <div className="mt-5 flex flex-wrap items-center gap-2">
                <input
                  type="password"
                  value={keyInput}
                  autoComplete="off"
                  spellCheck={false}
                  onChange={e => setKeyInput(e.target.value)}
                  onKeyDown={e => {
                    if (e.key === 'Enter') saveKey()
                    if (e.key === 'Escape') {
                      setEditingKey(false)
                      setKeyInput('')
                    }
                  }}
                  placeholder="Paste your api.bible key"
                  aria-label="api.bible key"
                  className="min-w-0 flex-1 rounded border border-outline-variant bg-surface-container-lowest px-3 py-2 font-mono text-mono-ui text-on-surface placeholder:text-slate-muted focus:border-primary focus:outline-none"
                />
                <button
                  type="button"
                  onClick={saveKey}
                  disabled={savingKey || keyInput.trim().length === 0}
                  className="rounded bg-primary px-6 py-2 font-mono text-mono-ui uppercase text-on-primary transition-all hover:shadow-[0_0_20px_rgba(173,198,255,0.3)] disabled:cursor-not-allowed disabled:opacity-40"
                >
                  {savingKey ? 'Saving' : 'Save key'}
                </button>
                {hasKey && (
                  <button
                    type="button"
                    onClick={() => {
                      setEditingKey(false)
                      setKeyInput('')
                    }}
                    className="rounded border border-outline-variant px-4 py-2 font-mono text-mono-ui uppercase text-slate-muted transition-colors hover:border-primary/40"
                  >
                    Cancel
                  </button>
                )}
                <p className="w-full font-mono text-mono-ui text-slate-muted">
                  Get a free key at scripture.api.bible, then paste it here.
                </p>
              </div>
            )}

            {online.length > 0 && (
              <ul className="mt-6 flex flex-wrap gap-2 border-t border-outline-variant/30 pt-5">
                {online.map(t => (
                  <li
                    key={t.id}
                    title={`${t.name} — ${hasKey ? 'available' : 'needs a key'}`}
                    className={`rounded border px-3 py-1.5 font-mono text-mono-ui ${
                      hasKey
                        ? 'border-primary/30 bg-primary/5 text-on-surface'
                        : 'border-outline-variant text-slate-muted opacity-60'
                    }`}
                  >
                    {t.id}
                    <span className="ml-2 uppercase text-slate-muted">
                      {hasKey ? 'Ready' : 'No key'}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </div>

          {/* Offline Bibles need no configuration; listing them says what still works
              with the network down, which is the question a key prompt raises. */}
          <p className="mt-3 font-mono text-mono-ui text-slate-muted">
            Always available offline: {offline.map(t => t.id).join(', ') || '—'}
          </p>
        </SettingsSection>

        {/* --- Audio -------------------------------------------------------- */}
        <SettingsSection
          title="Audio Input"
          caption="Capture"
          description="Which microphone the engine listens to. The device can only be changed while listening is stopped."
        >
          <div className="flex flex-wrap items-center justify-between gap-4 rounded-xl border border-outline-variant bg-surface-container p-6">
            <div>
              <p className="font-bold text-on-surface">Input device</p>
              <p className="text-body-md text-on-surface-variant">
                {status?.listening
                  ? 'Stop listening to change the device.'
                  : 'Defaults to whatever the system is using.'}
              </p>
            </div>
            <select
              value={status?.deviceId ?? ''}
              disabled={status?.listening}
              onChange={e => selectDevice(e.target.value)}
              className="max-w-xs rounded border border-outline-variant bg-surface-container-lowest px-3 py-2 font-mono text-mono-ui text-on-surface focus:border-primary focus:outline-none disabled:cursor-not-allowed disabled:opacity-40"
            >
              <option value="">System default</option>
              {devices.map(d => (
                <option key={d.id} value={d.id}>
                  {d.name}
                  {d.isDefault ? ' — default' : ''}
                </option>
              ))}
            </select>
          </div>

          <div className="mt-4 rounded-xl border border-outline-variant bg-surface-container-high p-6">
            <div className="mb-6 flex items-start justify-between gap-4">
              <div>
                <h3 className="text-body-lg font-bold text-on-surface">Speech threshold</h3>
                <p className="text-body-md text-on-surface-variant">
                  How loud the room has to get before the engine treats it as talking. Set it
                  above your room&rsquo;s background noise but below the speaker&rsquo;s voice:
                  too low and the hum never counts as a pause, so sentences run together and
                  arrive late; too high and quiet speech is missed entirely.
                </p>
              </div>
              <div className="shrink-0 rounded border border-outline-variant bg-navy-deep px-3 py-1 text-center">
                <span className="font-display text-headline-md text-primary">
                  {(vadThreshold / 1000).toFixed(3)}
                </span>
                <span className="ml-1 font-mono text-[10px] uppercase text-slate-muted">RMS</span>
              </div>
            </div>
            <Slider
              // Thousandths: the server clamps to 0.001–0.1, so the track spans the same.
              min={1}
              max={VAD_MAX}
              value={vadThreshold}
              onChange={setVadThreshold}
              onCommit={v =>
                void api
                  .setVadThreshold(v)
                  .then(r => setVadThreshold(Math.round(r.vadThreshold * 1000)))
                  .catch(fail)
              }
              lowLabel="Sensitive (quiet rooms)"
              highLabel="Strict (noisy rooms)"
              ariaLabel="Speech energy threshold"
            />
            <div className="mt-6 flex gap-3 rounded-lg border border-outline-variant/30 bg-navy-deep/50 p-4">
              <Icon name="graphic_eq" size={20} className="text-emerald-live" />
              <p className="text-mono-ui italic leading-tight text-on-surface-variant">
                {status?.listening ? (
                  <>
                    Your input is reading{' '}
                    <span className="not-italic text-on-surface">{level.rms.toFixed(3)}</span> right
                    now. Stay quiet for a moment and set the threshold a little above the number you
                    see.
                  </>
                ) : (
                  <>Start listening to see your room&rsquo;s current level here while you adjust.</>
                )}
              </p>
            </div>
          </div>
        </SettingsSection>
      </div>
    </div>
  )
}
