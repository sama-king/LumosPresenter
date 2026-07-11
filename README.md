# LumosPresenter

Offline-first app that listens to live microphone audio, detects spoken Bible references,
retrieves verses from a local SQLite database, and publishes them to a webpage on the local
network for projection or OBS.

## Documentation

- [Architecture overview](docs/architecture-overview.md) — system, pipeline, API surface, SSE contract
- [Reference parser design](docs/reference-parser.md) — grammar, sticky context, cue libraries, confidence model
- [Speech engines & audio](docs/speech-and-audio.md) — Whisper/sherpa-onnx, models, VAD, capture, media files
- [Database architecture](docs/database-architecture.md) — schema, versioning, importer pattern, future media
- [Original implementation plan](docs/AI_Implementation_Plan_Bible_Display_1.md)
- [Design reference](docs/Design.md)
- [UI references](stitch_designs/)

## Architecture

One self-contained ASP.NET Core process serving two pages:

- **Operator console** — `http://localhost:5170/admin`
- **Display page** — `http://<machine-ip>:5170/display`

Clean Architecture: all abstractions and domain logic live in `Core` (dependency-free);
vendor- and platform-specific code sits behind interfaces in the outer projects and is
composed via DI in `WebHost`.

| Project | Responsibility |
|---|---|
| `src/LumosPresenter.Core` | Reference parser, domain models, abstractions (`ISpeechEngine`, `IAudioCapture`, `IVerseRepository`) — pure, no dependencies |
| `src/LumosPresenter.Speech` | Whisper.net + sherpa-onnx engine implementations, benchmark harness |
| `src/LumosPresenter.Audio` | PortAudio microphone capture, VAD, mic-permission handling |
| `src/LumosPresenter.Data` | Dapper repository over SQLite; bundled translations + EasyWorship import |
| `src/LumosPresenter.WebHost` | Minimal API, SSE push, serves the built frontend from `wwwroot` |
| `frontend` | React + Vite (operator console + display), builds into `WebHost/wwwroot` |
| `tests/LumosPresenter.Tests` | xUnit — parser corpus, golden-audio pipeline, import tests |

## Prerequisites

- .NET SDK 10.0.3xx (see `global.json`)
- Node.js 22+ (build-time only; no Node in production)

## Development

```bash
# Backend (serves on http://0.0.0.0:5170)
dotnet run --project src/LumosPresenter.WebHost

# Frontend dev server with HMR (proxies /api, /events to :5170)
cd frontend && npm run dev

# Frontend production build → emitted into WebHost/wwwroot
cd frontend && npm run build

# Tests
dotnet test
```

## Packaging (from a Mac, both platforms)

```bash
cd frontend && npm run build && cd ..
dotnet publish src/LumosPresenter.WebHost -c Release -r osx-arm64 --self-contained
dotnet publish src/LumosPresenter.WebHost -c Release -r win-x64 --self-contained
```

## Notes

- **Port**: the plan document says 5000, but macOS AirPlay Receiver listens on 5000
  by default, so the app defaults to **5170** (`Urls` in `appsettings.json`).

- **Target framework** is set once in `Directory.Build.props` (currently `net10.0` LTS;
  the plan document says .NET 8 — retarget there if a native package requires it).
- Bundled translations are public domain only (KJV, WEB, ASV). Copyrighted translations
  enter only via the operator's own EasyWorship database import.
