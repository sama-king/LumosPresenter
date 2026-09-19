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
# Bundled translations (KJV, ASV, BSB) are committed in src/LumosPresenter.WebHost/data/seed
# and imported on first start. To refresh them from upstream (re-verified before writing):
#   dotnet run scripts/fetch-seed-bibles.cs -- --force

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

## Packaging (macOS, Windows, Linux)

One script builds every platform:

```bash
src/LumosPresenter.Launcher/package.sh osx-arm64   # or osx-x64
src/LumosPresenter.Launcher/package.sh win-x64     # or win-arm64
src/LumosPresenter.Launcher/package.sh linux-x64   # or linux-arm64
```

| Platform | Output in `artifacts/` |
|---|---|
| macOS | `LumosCast.app` and `LumosCast-<version>-<rid>.zip` |
| Windows | `LumosCast-<rid>/`, a folder that runs as-is, plus `LumosCast-<version>-<rid>-setup.exe` when Inno Setup is installed |
| Linux | `LumosCast-<rid>/` (with a `.desktop` entry and icon) and `LumosCast-<version>-<rid>.tar.gz` |

It needs .NET, Node/npm and bash (Git Bash on Windows). The translation seeds and default
backgrounds are in the repository; the one thing to provide before the first run is the
default speech model named by `Speech:ModelPath` in `appsettings.json` (models are too large
to commit). The script stops with an error when the model or the seeds are missing, rather
than build a package that cannot start listening or read scripture.

What every package gets, in one place so the platforms cannot drift apart:

- **A fresh console.** The frontend is rebuilt into `WebHost/wwwroot` first
  (`--skip-frontend` to reuse the last build).
- **Launcher and WebHost in one directory.** `ServerProcess` starts the server by looking
  for it beside its own executable, so a nested layout leaves the launcher up with no
  server behind it.
- **All the default backgrounds.** The script checks the count against `assets/backgrounds/`.
- **Two Whisper models**, the default and `base.en`, so switching model in the console
  works without a download, plus `sherpa-onnx` when present, and the CoreML encoders on macOS.
- **This platform's natives only.** Foreign-platform libraries are pruned, but the GPU
  variants stay: Vulkan on Windows and Linux, CoreML on macOS.
- **The translation seeds, not this machine's database.** Each install builds a clean
  `data/lumos.db` on first start. `--with-database` ships the build machine's database
  instead (songs, settings, api.bible key and all), which suits your own installs only.

The version comes from `<Version>` in `LumosPresenter.Launcher.csproj`; the script stamps it
into the macOS bundle and the Windows installer.

`dotnet publish -r` cross-compiles, so any host can build any platform, with two limits:
the Windows installer needs `iscc`, which runs only on Windows, and a macOS or Linux
package built on Windows loses the executable bit. Build those on a Mac or Linux machine.

On macOS the dock icon comes only from the bundle's `CFBundleIconFile`, which no runtime
API can set, so `dotnet run` shows the generic .NET icon and the packaged `.app` the brand.
On Linux, *Add from Disk* opens the system file dialog through `zenity` or `kdialog`;
with neither installed it falls back to the console's own file browser.

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

### Windows installer

With [Inno Setup 6](https://jrsoftware.org/isdl.php) installed, `package.sh win-x64` compiles
`package-windows.iss` itself and writes `artifacts/LumosCast-<version>-win-x64-setup.exe`.
To compile it by hand (on Windows; `iscc` does not run elsewhere):

```
iscc /DAppVersion=1.0.0 /DRid=win-x64 src\LumosPresenter.Launcher\package-windows.iss
```

Uninstalling leaves `data/` behind on purpose: `lumos.db` holds the operator's imported
songs and cached translations, so removing it would destroy their library.

The installer is deliberately per-user (`%LOCALAPPDATA%`, `PrivilegesRequired=lowest`):
the WebHost writes `data/lumos.db` inside its own install directory, which a standard
user cannot do under `%ProgramFiles%` — installed there it would run but never save.

## Logs

Both live in `logs/`, beside the app — inside `LumosCast.app/Contents/MacOS/` on
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
