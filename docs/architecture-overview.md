# Architecture Overview

*The system as currently implemented. Companion docs:
[reference-parser.md](reference-parser.md), [speech-and-audio.md](speech-and-audio.md),
[database-architecture.md](database-architecture.md), and the original
[implementation plan](AI_Implementation_Plan_Bible_Display_1.md).*

## One process, two pages

A single self-contained ASP.NET Core process (`LumosPresenter.WebHost`) serves:

- **Operator console** — `http://localhost:5170/admin`
- **Display pages** — `http://<machine-ip>:5170/display/{id}` (projection / OBS), one per
  configured display; `/display` redirects to `/display/1`. Styling per display is set on
  the Stage page (`/stage`)

The React frontend builds into `wwwroot`; there is no Node in production. Port 5170
(not the plan's 5000) because macOS AirPlay Receiver occupies 5000.

## The pipeline

```
microphone ──┐
             ├─→ active ISpeechEngine ─→ TranscriptSegments ─→ ReferenceParser
media file ──┘        (whisper |            (partials +            │
 (paced/fast)          sherpa-onnx)          finals)               ▼
typed /simulate ──────────────────────────────────────→ BibleReference(s)
                                                               │
                                              IVerseRepository lookup (active translation)
                                                               │
                                                               ▼
                                    EventBroadcaster ─→ SSE /events ─→ admin + display
```

`TranscriptionPipeline` (WebHost) owns this flow: one parser instance (so typed test
input, file transcription, and live audio share chapter/verse context), start/stop,
engine switching, and verse-text resolution per detected reference.

## Projects (dependencies point inward)

| Project | Responsibility |
|---|---|
| `Core` | Pure, dependency-free: domain models, `ISpeechEngine` / `IAudioCapture` / `IVerseRepository` abstractions, the reference parser, VAD, bias vocabulary, WAV read/write |
| `Speech` | Whisper.net + sherpa-onnx engines, engine provider, options |
| `Audio` | PortAudio capture, device enumeration, level metering |
| `Data` | SQLite (Dapper): migrations, verse repository, scrollmapper importer, seeding |
| `WebHost` | Composition root: Minimal API, SSE broadcaster, pipeline, media decode, session recorder |
| `frontend` | React + Vite (unstyled test UI — real UI is a later phase) |
| `Tests` | xUnit: parser corpus, VAD, data layer, golden-audio engine integration |

## HTTP API

| Endpoint | Purpose |
|---|---|
| `GET /healthz` | liveness |
| `GET /api/status` | listening, engine, device, recording, window, translation |
| `POST /api/listening/start` · `/stop` | mic pipeline control |
| `POST /api/engine/{name}` | switch `whisper` ↔ `sherpa-onnx` (restarts pipeline) |
| `GET /api/whisper/models` · `POST /api/whisper/model` | list / hot-swap ggml models |
| `POST /api/parser/window/{n}` | sticky-context utterance window (default 15) |
| `GET /api/translations` · `POST /api/translation/{code}` | list / switch active translation |
| `GET /api/audio/devices` · `POST /api/audio/device` | input device picker |
| `GET /api/audio/level` | live peak/RMS + clipping flag |
| `POST /api/audio/recording/{bool}` · `GET /api/audio/recording/latest` | session WAV dump |
| `POST /api/media/upload` | audio-only upload (≤ 200 MB) → decode to 16 kHz WAV |
| `GET /api/media/{id}/audio` | seekable playback of the original |
| `POST /api/media/{id}/transcribe[?fast=true]` | run file through the pipeline (paced / fast) |
| `POST /api/simulate` | typed utterance through the same parser + verse lookup |
| `GET /api/scripture/search?q=` | synchronous reference parse + verse lookup (fresh parser, no live context) |
| `GET /api/scripture/chapter/{book}/{chapter}[?translation=]` | full chapter for the Context Preview |
| `POST /api/live` · `GET /api/live` · `POST /api/live/clear` | push / read / clear what the displays show (`LiveState`) |
| `GET /api/fonts` | font registry for the display font pickers (data-driven; future settings page can add fonts) |
| `GET /api/displays` · `GET /api/displays/{id}` | list (with default config) / read displays with their effective config |
| `POST /api/displays` | add display; optional `useSettingsOfDisplayId` creates a persistent follow link |
| `PUT /api/displays/{id}/config` | save styling (validated); 400 while the display follows another |
| `PUT /api/displays/{id}/source` | set/detach the follow link (detach snapshots the source's config) |
| `POST /api/displays/{id}/reset` | reset styling to the code default |
| `DELETE /api/displays/{id}` | delete (last display blocked; followers detached with snapshot) |

## SSE stream (`GET /events`)

One-way, per the plan. Event types:

- `transcript` — `{text, isFinal, source, at}`; partials (sherpa) update live, finals append
- `reference` — `{display, book, chapter, verseStart, verseEnd, confidence, utterance, translation, text}`
- `status` — `{listening, engine}` on pipeline state changes
- `translation` — `{translation}` when the active translation switches
- `live` — `{id, reference, text, translation, source, at}` (or `{cleared: true}`) — the
  single channel displays render; fed by `POST /api/live` (manual) and the pipeline's
  server-side confidence-gated auto flow (`Parser:AutoLiveConfidence`, default 0.75), so
  detections reach the displays regardless of which console page — if any — is open.
  `LiveState` suppresses duplicate pushes (same reference/text/translation)
- `displayconfig` — `{displayId, config}` when a display's stage configuration changes;
  followers of an edited display each get their own event, so open display windows
  restyle live by filtering on their id alone
- `pipelineerror` — `{message}` when the pipeline fails (mic missing, model missing, …)

## Key cross-cutting decisions

- **.NET 10 / `net10.0`** target, set once in `Directory.Build.props` (plan said 8; 10 is
  the newer LTS and what's installed — retarget is a one-line change).
- **Everything testable without hardware**: `/simulate` for the parser, fast file
  transcription for engines, fake `IAudioCapture` in tests, golden-audio integration tests
  that no-op when models aren't downloaded.
- **Confidence gates everything**: inferred detections score below 0.9 so a future confirm
  mode can queue them for one-click operator approval before display.
- **Large assets are gitignored and fetched**: speech models (`models/`), bible seeds
  (`data/seed/`), uploads (`media/`), the database itself (`data/`).

## Not yet built (from the plan)

Confirm-mode queue and manual override · EasyWorship import · display history persistence ·
duplicate suppression/upgrade logic on the display · engine bake-off harness with WER
scoring · designed UI for Songs/Media/Settings (Scripture console and Stage configuration
are built; the legacy test console lives at `/admin`) · image/motion display backgrounds
and font installing (registry supports them; UI ships solid colors + 8 bundled fonts) ·
verse auto-fit on the display (fixed size clips to the viewport today) · packaging (`dotnet publish` single-file for osx-arm64 /
win-x64, mic permission in a packaged .app) · CI golden-audio job · cross-platform media
decode (ffmpeg).
