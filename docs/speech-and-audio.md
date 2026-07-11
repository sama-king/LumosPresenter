# Speech Engines & Audio Capture

*Implemented in `LumosPresenter.Speech`, `LumosPresenter.Audio`, and
`LumosPresenter.Core/Audio`.*

## Engine abstraction

`ISpeechEngine` (Core) consumes `IAsyncEnumerable<AudioFrame>` and yields
`TranscriptSegment`s. The parser consumes text, not audio, so engines are interchangeable
at runtime without touching anything downstream. `ISpeechEngineProvider` holds the active
engine; the operator console switches it live (`POST /api/engine/{name}`), which restarts
the pipeline.

| | `whisper` (Whisper.net / whisper.cpp) | `sherpa-onnx` (streaming Zipformer) |
|---|---|---|
| Mode | Non-streaming: VAD-chunked utterances, finals only | Streaming: word-level partials + endpoint finals |
| Latency | ~3–4 s after a pause (chunk + inference) | < 2 s, partials near-instant |
| Quality | Higher, especially accented speech with larger models | Lower (20M-param model), acceptable for book names/numbers |
| Models | ggml files in `models/whisper/`, hot-swappable via `POST /api/whisper/model` | Zipformer encoder/decoder/joiner + tokens in `models/sherpa-onnx/` |

Both load models lazily on first use and throw clear errors (with download pointers) if
model files are missing. Model files are gitignored; see `appsettings.json` → `Speech:`.

### Installed Whisper models (A/B from the admin dropdown)

`base.en` (142 MB) → `small.en` (466 MB, **default**) → `small` multilingual →
`medium.en` (1.5 GB) → `large-v3-turbo-q5_0` (~570 MB). For accented speech
(Ghanaian English being the target), bigger encoders win: `large-v3-turbo` keeps the full
large-v3 encoder with a pruned decoder, so it's both stronger on accents *and* ~4–6×
faster than `medium`. Decide with real recordings, not benchmarks.

### Vocabulary biasing

Only book names and numbers must survive transcription, so both engines are biased toward
scripture vocabulary (`BiasVocabulary`, built from the parser's `BookCatalog`):

- Whisper: initial prompt (`WithPrompt`), on by default (`Speech:Whisper:BiasPrompt`).
- sherpa-onnx: hotwords support exists in the library (not yet wired).

## Audio capture (`LumosPresenter.Audio`)

- **PortAudio** (`PortAudioSharp2`) — cross-platform; NAudio is deliberately excluded
  (Windows-only). Produces 16 kHz mono float frames, the input format both engines expect.
- Device enumeration + selection (`IAudioDeviceEnumerator`, `IAudioCapture.DeviceId`),
  live level metering (peak/RMS + clipping flag) polled by the admin page.
- Bounded channel with drop-oldest: a stalled consumer loses old audio instead of memory.
- macOS: the OS prompts for mic access on first use; a **packaged** .app must declare
  `NSMicrophoneUsageDescription` or capture silently records nothing (TCC).

## VAD (`Core/Audio/VoiceActivityChunker`)

Pure, unit-tested energy-based chunker used by the Whisper path: RMS threshold, 200 ms
pre-roll (word onsets not clipped), 300 ms minimum speech, 600 ms end-silence, 8 s max
utterance. Lives in Core (not Audio) because it's pure logic the Speech project needs and
Speech only references Core.

## Debugging what the engine hears

- **Session recorder**: toggle on the admin page; the full mic session is written to a WAV
  (`debug-recordings/`) and downloadable — the fastest way to distinguish an audio-capture
  problem (quiet/clipped/reverberant/wrong device) from a model-accuracy problem.
- **Level meter**: aim for healthy green on speech; red = clipping = garbage transcripts.
- Physical chain matters more than models: close-mic the speaker or take a direct feed
  from the mixing console; avoid room mics capturing PA bleed and reverb.

## Media-file transcription

Upload an **audio** file (≤ 200 MB; video is rejected with guidance to extract audio) →
decoded to 16 kHz mono WAV → run through the *same* engine/parser/SSE path as the mic.

- **Paced mode** ("Play + transcribe"): frames fed at real time so browser playback and
  captions roughly track.
- **Fast mode** ("Transcribe fast"): frames fed as fast as the engine consumes — a ~4.5 s
  clip transcribes in under a second with a warm `small.en`; ideal for iterating on models
  and parser changes against real recordings.

Decoding uses macOS's built-in `afconvert` (offline, no dependency). Two known limits:
`.opus` (Ogg-contained Opus — WhatsApp/Telegram voice notes) is **not** decodable by
Core Audio, and Windows has no `afconvert` — both are solved by the planned switch to an
installed `ffmpeg` with `afconvert` fallback.

## Latency expectations (measured on this machine)

- Simulated text → reference event: milliseconds.
- Fast file transcribe (warm small.en): ~5× real time.
- Live whisper path: bounded by VAD chunking — expect the transcript ~1–4 s after a pause.
- First use of any engine/model after startup pays a one-time model-load cost (seconds).
