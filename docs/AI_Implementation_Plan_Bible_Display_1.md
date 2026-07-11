# AI Implementation Plan — Bible Reference Detection & Live Scripture Display

## Mission

Build a production-quality, offline-first application that listens to live microphone audio, detects spoken Bible references, retrieves verses from a local SQLite Bible database, and instantly publishes them to a webpage hosted on the local network for projection or OBS.

## Constraints

- Build, run, debug, test, and package on **macOS** (Apple Silicon, primary platform).
- Produce **Windows** builds from the same codebase on a Mac.
- Fully **offline** at runtime. No network dependency.
- **Free and open-source** components only. The single optional paid item is an Apple Developer account for signed/notarized macOS distribution; the app runs unsigned locally at zero cost.

## Objectives

- Offline-first operation.
- Low end-to-end latency (target set per speech engine — see below).
- Single cross-platform codebase, buildable from macOS.
- Native macOS and Windows deployment.
- Modular, dependency-injected, testable architecture (Clean Architecture).
- Real-time browser updates.
- Speech-to-text engine swappable at runtime between Whisper and sherpa-onnx.

## Architecture — Single ASP.NET Core Process

The application is one self-contained ASP.NET Core process (.NET 8 LTS) that serves two browser pages:

- **Operator console** at `http://localhost:5000/admin` — start/stop listening, choose translation, manual override, confirm-mode approvals, history, settings, engine selection, database import.
- **Display page** at `http://<machine-ip>:5000/display` — the projection/OBS surface, updated live.

No desktop UI framework is used. An optional tray shim or `launchd`/console launcher may start the process.

All platform- and vendor-specific code (audio capture, speech engines) sits behind interfaces. Business logic is engine-agnostic and lives in a pure `Core` library. Dependency injection is used throughout.

## Speech Engine Strategy — Swappable

A single `ISpeechEngine` abstraction has two concrete implementations, selectable via configuration, CLI flag, or the operator console **without recompiling**:

- **Whisper** via [Whisper.net](https://github.com/sandrohanea/whisper.net) — free NuGet package bundling prebuilt whisper.cpp binaries with CoreML/Metal acceleration on Apple Silicon. Models: `base.en` or `small.en`. Highest transcription quality; non-streaming.
- **sherpa-onnx** — free, Apache-2.0, official C# bindings, streaming Zipformer. Lower latency; quality sufficient because only book names and numbers must survive. Ships PortAudio-based microphone examples.

The parser downstream consumes text, not audio, so switching engines requires no parser changes. A benchmark harness feeds identical golden audio through both engines and reports latency and reference-detection accuracy side by side.

## Latency Targets (Per Engine)

- **Whisper path:** ~3–4 second end-to-end (VAD-triggered chunks with slight overlap). Whisper is non-streaming, so latency is bounded by chunk length plus inference.
- **sherpa-onnx path:** <2 second end-to-end (streaming with word-level partials).

The Week 1 engine bake-off sets the committed latency figure for the shipped default.

## Audio Capture

Use **PortAudio** via `PortAudioSharp2` for microphone capture on both platforms. (NAudio is Windows-only and must not be used.) Implement voice-activity detection to trigger chunking. Handle macOS microphone permission (`NSMicrophoneUsageDescription` / TCC) and verify it works in a **packaged** build, not only in development.

## Bible Data & Import

**Bundled translations (public domain only):** KJV, WEB (World English Bible), ASV. Use ready-made free SQLite datasets from [scrollmapper/bible_databases](https://github.com/scrollmapper/bible_databases). Do not ship copyrighted translations (NIV, ESV, NKJV, etc.).

**EasyWorship database import:** Provide an import path that reads an EasyWorship Bible database file and loads its translations into the local SQLite store. This lets operators bring translations they already own — including licensed/copyrighted ones — onto their own machine without the application redistributing any copyrighted text. Inspect the EasyWorship database format (EasyWorship 6/7 stores data in SQLite `.db` files), map its book/chapter/verse structure to the internal schema, and expose the import from the operator console (select file → validate → map → import → select as active translation). Handle version and schema differences defensively and report clear errors on unrecognized files.

Optional SQLite FTS5 may be added later if free-text verse search becomes a feature.

## Reference Parser — Build First

The reference parser is the highest-risk component and is built in Week 1, then matured across the project. It must handle:

- Ordinals: "First / 1st / I Corinthians".
- Abbreviations and common mis-transcriptions: "Filipians" → Philippians.
- References split across utterances: "turn to John chapter three… verse sixteen".
- Ambiguity: "Psalm one nineteen" (119 vs 1:19).

Components: a 66+ book alias table with fuzzy matching; spoken-number normalization (words and digits); stateful chapter/verse context so "verse seventeen" continues the prior chapter; and a confidence score that feeds the confirm gate. It is pure logic validated by a large xUnit corpus.

**Confirm mode:** detected verses queue for one-click operator approval before appearing on the display page. Recommended default for live services.

## Data Layer

SQLite accessed with **Dapper** (or raw `Microsoft.Data.Sqlite`). Read-only lookup tables: translations, books, verses, aliases. Do not use EF Core.

## Real-Time Transport

Verse pushes are strictly one-way. Use **Server-Sent Events (SSE)** as the default (minimal, no client library). SignalR is an acceptable free alternative if bidirectional features are later required.

## Frontend

**React + Vite**, built to static assets served from the ASP.NET Core `wwwroot`. No Node process in production. Covers both the operator console and the display page.

## Project Layout

| Project | Responsibility |
|---|---|
| `Core` | Reference parser, domain models, confidence logic — pure, dependency-free |
| `Speech` | `ISpeechEngine` + Whisper.net and sherpa-onnx implementations + benchmark harness |
| `Audio` | PortAudio capture, VAD, macOS/Windows mic-permission handling |
| `Data` | Dapper repository over SQLite; bundled data + EasyWorship import |
| `WebHost` | Minimal API, SSE/SignalR, serves display + operator pages from `wwwroot` |
| `Frontend` | React + Vite (operator console + display), built to static assets |
| `Tests` | xUnit — parser corpus, golden-audio pipeline, import tests, smoke test |

## Functional Requirements

Continuous listening; runtime-swappable transcription engine (Whisper ↔ sherpa-onnx); Bible reference detection with fuzzy book-alias matching; spoken-number normalization; stateful reference context; confidence scoring; confirm mode; duplicate suppression; offline verse retrieval; translation selection; **EasyWorship database import**; local network hosting; live one-way browser updates; manual override; history; structured logging (Serilog); settings.

## Testing

Use xUnit. Concentrate effort on the parser (hundreds of text cases) and a golden-audio set run through the full pipeline in CI. Include import tests against sample EasyWorship files. A single browser smoke test covers the one-page display; do not use Playwright. CI runs on GitHub Actions (free for public repos).

## Packaging

`dotnet publish -r osx-arm64 --self-contained` and `dotnet publish -r win-x64 --self-contained`, both run from a Mac, producing single-file self-contained executables with bundled models. Signed/notarized macOS distribution requires an Apple Developer account (~$99/year); otherwise run unsigned locally (right-click → Open, or `xattr -dc`).

## Schedule

- **Week 1 — De-risk.** Build the reference parser + xUnit corpus. PortAudio capture spike including macOS mic permission in a packaged build. ASR bake-off: implement `ISpeechEngine` with both engines, run identical golden audio, decide the default and committed latency.
- **Week 2 — Pipeline.** Wire audio → engine → parser → SQLite retrieval end to end. Duplicate suppression, confidence gating. Implement EasyWorship import and schema mapping.
- **Week 3 — Interfaces.** Operator console and display page, confirm mode, translation selection, settings, logging, SSE/SignalR push.
- **Week 4 — Ship.** Self-contained packaging for both platforms, golden-audio CI, documentation, smoke test, polish.

## Risk Assessment

- **Latency vs engine mismatch** — swappable engine; target set per engine after the bake-off.
- **Parser accuracy on messy speech** — built first with a large corpus; live output gated by confirm mode.
- **Cross-platform audio** — PortAudio (not NAudio); macOS permission tested in a packaged build early.
- **EasyWorship format variance** — inspect the format early, validate on import, fail clearly on unrecognized files.
- **Bible licensing** — bundle only public-domain translations; copyrighted translations arrive only via user-owned import.
- **Packaged-app permission failures on macOS** — surfaced in Week 1, not Week 4.

## Avoid

WPF, WinForms, WinUI, Avalonia, COM, Registry dependencies, IIS, NAudio, EF Core, Playwright, Electron/Node in production, any Windows-only API, and shipping any copyrighted Bible translation.

## Deliverables

Architecture specification; component, sequence, and deployment diagrams; database schema; API specification (Minimal API + SSE/SignalR contract); parser design (alias grammar, number normalization, confidence model); EasyWorship import specification; ASR bake-off report; UI wireframes (operator console + display); four-week implementation plan; risk assessment; testing strategy; deployment/packaging guide; roadmap.

## Success Criteria

A developer with only a Mac can build, debug, test, and package the application and produce Windows builds from the same codebase. At runtime the operator can switch the transcription engine between Whisper and sherpa-onnx, import an EasyWorship Bible database, and see the correct verse appear on the network display page within the committed latency target — detected and optionally approved — entirely offline and at zero required cost.
