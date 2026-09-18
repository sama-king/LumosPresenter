<picture>
  <source media="(prefers-color-scheme: dark)" srcset="assets/brand/out/lockup-on-dark.png">
  <img src="assets/brand/out/lockup-on-light.png" alt="LumosPresenter" height="72">
</picture>


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
| `src/LumosPresenter.Launcher` | Avalonia desktop launcher: logo, links to Control / Screen 1 / Screen 2, microphone picker; window + system tray |
| `frontend` | React + Vite (operator console + display), builds into `WebHost/wwwroot` |
| `tests/LumosPresenter.Tests` | xUnit — parser corpus, golden-audio pipeline, import tests |

## Prerequisites

- .NET SDK 10.0.3xx (see `global.json`)
- Node.js 22+ (build-time only; no Node in production)

## Development

```bash
# Bundled translations (KJV, ASV, BSB) — run once per clone. Downloads ~14 MB into
# src/LumosPresenter.WebHost/data/seed, which the WebHost imports on first start.
# Without it the app starts but every scripture lookup comes back empty.
dotnet run scripts/fetch-seed-bibles.cs

# Backend (serves on http://0.0.0.0:5170)
dotnet run --project src/LumosPresenter.WebHost

# Frontend dev server with HMR (proxies /api, /events to :5170)
cd frontend && npm run dev

# Frontend production build → emitted into WebHost/wwwroot
cd frontend && npm run build

# Desktop launcher (window + tray). Starts the WebHost itself unless one is already
# running on the port, so this alone is enough; --port to target a different one.
dotnet run --project src/LumosPresenter.Launcher

# Tests
dotnet test
```

## Translations

Bundled and public-domain: **KJV**, **ASV**, **BSB** — always available offline.

**NIV**, **AMP** and **MSG** come from [api.bible](https://scripture.api.bible) and are
cached locally for 14 days per chapter (the window slides on every reference). Each
operator supplies their own free key from [scripture.api.bible](https://scripture.api.bible)
and enters it on the console's **Settings** page (the gear in the header), under *Online
Bible Sources*; it is stored in the database, takes effect immediately, and can be changed
or removed at any time.

Until a key is set the online translations are shown but not selectable, and the *Enable
Online Sources* toggle in the Scripture page's Bibles band is disabled — the bundled translations are unaffected. For
development, `API_BIBLE_KEY` in a `.env` at the repository root (or `ApiBible__Key` in the
environment) is still honoured: it is adopted into the database once, on a database that
has no key yet, after which the stored key is authoritative. The console marks each
translation **Offline** or **Online** so an operator knows what is safe to rely on without
a connection.

## Packaging (from a Mac, both platforms)

```bash
cd frontend && npm run build && cd ..
dotnet publish src/LumosPresenter.WebHost -c Release -r osx-arm64 --self-contained
dotnet publish src/LumosPresenter.WebHost -c Release -r win-x64 --self-contained

# The launcher is the app the operator runs; publish it into the same folder so it can
# find and start the WebHost beside it.
dotnet publish src/LumosPresenter.Launcher -c Release -r osx-arm64 --self-contained
dotnet publish src/LumosPresenter.Launcher -c Release -r win-x64 --self-contained
```

On macOS the dock icon comes from the bundle's `CFBundleIconFile`, which no runtime API
can set — a bare `dotnet publish` shows the generic .NET icon. Build the `.app` instead:

```bash
src/LumosPresenter.Launcher/package-macos.sh osx-arm64
```

On Windows the same script exists as `package-windows.sh`, which cross-compiles from
macOS or Linux and writes `artifacts/LumosCast-win-x64/` — a self-contained folder that
runs as-is:

```bash
src/LumosPresenter.Launcher/package-windows.sh win-x64
```

Both scripts publish the launcher and the WebHost into the *same* directory on purpose:
`ServerProcess` starts the server by looking for it beside its own executable, so a
nested layout leaves the launcher up with no server behind it.

Neither script builds the frontend — they publish whatever is already in
`WebHost/wwwroot`. Run `npm run build` in `frontend/` first, or the package ships the
console as it was at the last build, which can be older than the server beside it.

### Default backgrounds

The images in [`assets/backgrounds/`](assets/backgrounds/) ship with every package and
appear in the Stage tab's background picker on first start. To change the set, change the
folder — there is no other step:

- **Add** a `.jpg`, `.png`, `.webp` or `.gif` (or a `.mp4`, `.webm` or `.mov` for a
  looping motion background). Use lower-case extensions; the build matches on them.
- **Name it for what it shows** — `mountain-lake-reflection.jpg` appears to the operator
  as "Mountain Lake Reflection". The filename is also the background's permanent id, so
  **do not rename one after it has shipped**: existing installs would get the renamed copy
  as a new background alongside the old one.
- **Prefer 1920×1080 or larger, landscape.** Anything smaller is upscaled on a projector.
- **Rebuild the package** as above.

This works because `LumosPresenter.WebHost.csproj` links the folder into build and publish
output as `backgrounds/` beside the exe — so packaging picks it up without a script change
— and `BundledBackgroundSeeder` registers each file at startup. On an existing install:

| Change | Result on the operator's machine |
|---|---|
| File added to the folder | Appears after the update |
| Operator deletes a default | Stays deleted; it is not re-added at the next start |
| File removed from the folder | Stays on installs that already have it |

To produce the installer, compile `package-windows.iss` with
[Inno Setup 6](https://jrsoftware.org/isdl.php) **on Windows** (`iscc` does not run on
macOS), which emits `artifacts/LumosCast-1.0.0-setup.exe`:

```
iscc src\LumosPresenter.Launcher\package-windows.iss
```

Uninstalling leaves `data/` behind on purpose: `lumos.db` holds the operator's imported
songs and cached translations, so removing it would destroy their library.

The installer is deliberately per-user (`%LOCALAPPDATA%`, `PrivilegesRequired=lowest`):
the WebHost writes `data/lumos.db` inside its own install directory, which a standard
user cannot do under `%ProgramFiles%` — installed there it would run but never save.

## Logs

Both live in `logs/`, beside the app — inside `LumosPresenter.app/Contents/MacOS/` on
macOS, in the install directory on Windows. The launcher's **Show logs** button (and the
tray's **Show Logs**) opens the folder.

| File | Written by | Holds |
| --- | --- | --- |
| `server-<date>.log` | The WebHost, via Serilog | Everything the server logs, rolled daily, 14 days kept |
| `launcher-server.log` | The launcher | The child's raw stdout and stderr, plus its start and exit lines |

The second file is the one that matters after a crash. Serilog only records what the app
manages to log, so it misses a native crash in the speech engine, an unhandled exception
on a background thread, and anything that fails before logging is configured — Kestrel
unable to bind the port, a missing model, a database that will not migrate. The launcher
captures the child's streams directly, so those land in `launcher-server.log` along with
the exit code.

## Brand assets

`assets/brand/source/` holds the original renders; every shipped icon is derived from them
by `assets/brand/generate.py`, so re-run it rather than editing an icon by hand:

```bash
python3 assets/brand/generate.py
```

It writes the launcher's tray icons, `AppIcon.icns` and `app.ico`, the console's favicons
and header marks, and the lockups used above. The renders have no alpha, so the script
recovers transparency by unmatting from the ground each variant was drawn on — see the
module docstring for why a colour key does not work here.

## Notes

- **Port**: the plan document says 5000, but macOS AirPlay Receiver listens on 5000
  by default, so the app defaults to **5170** (`Urls` in `appsettings.json`).

- **Target framework** is set once in `Directory.Build.props` (currently `net10.0` LTS;
  the plan document says .NET 8 — retarget there if a native package requires it).
- Bundled translations are public domain only (KJV, ASV, BSB). Copyrighted text is never
  redistributed with the app: it enters either via the operator's own EasyWorship import,
  or by being fetched with the operator's own api.bible key (NIV/AMP/MSG) and cached
  locally for 14 days.
