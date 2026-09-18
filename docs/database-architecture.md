# Database Architecture

*Approved 2026-07-07. Implemented in `LumosPresenter.Data`.*

## Summary

One SQLite file — `data/lumos.db` (WAL mode) — holds all structured data: bible content,
display history, and (later) song/media metadata. Access is via **Dapper** only; EF Core is
deliberately excluded (see the implementation plan). Media *files* (images, video, audio)
live on disk; only their metadata will enter the database.

## Three kinds of "version", kept distinct

| Concept | Mechanism |
|---|---|
| Bible translations (KJV vs BSB vs an imported Twi bible) | Rows in `translations`; adding one never changes schema |
| Database schema evolution | `schema_migrations` table + embedded SQL scripts (`Migrations/NNN_name.sql`) applied in order at startup by `MigrationRunner` |
| Bundled content revisions | `content_version` column on `translations`, so an app update can re-seed corrected bundled data without touching user-imported translations |

## Schema (migration 001)

```sql
translations (id, code UNIQUE, name, language, source, license, imported_at, content_version)
books        (number PK, name, chapter_count)          -- canonical 66, synced from BookCatalog
book_names   (translation_id, book_number, name)       -- per-translation display names
verses       (translation_id, book_number, chapter, verse, text, span_end)
             -- composite PK, WITHOUT ROWID: range lookups ride the PK index
             -- span_end (migration 005) is NULL for a normal verse; see remote translations
display_history (id, shown_at, kind, reference, detail) -- kind-generic from day one
```

Key points:

- **`books` is canonical and translation-agnostic**, synced at startup from the parser's
  `BookCatalog` so the parser and database can never disagree about book numbering.
- **`book_names`** preserves each translation's own naming (e.g. scrollmapper's
  "Revelation of John", or Twi names from a future import) for display purposes.
- **`verses` composite PK** `(translation_id, book_number, chapter, verse)` makes a verse or
  range lookup a single indexed `BETWEEN` — no secondary indexes needed.
- **`display_history.kind`** is `'verse'` today and `'song' | 'image' | 'video'` later —
  the history table never needs a schema change when media arrives.

## Schema (migration 002 — stage configuration)

```sql
fonts    (id, slug UNIQUE, name, css_family, source, css_url, weights, enabled, sort_order)
displays (id, name, sort_order, config_json, follows_display_id → displays.id,
          created_at, updated_at)
```

- **`fonts` is a registry**: 8 bundled fonts seeded by the migration; `source` is
  `'bundled'` today and `'file'` later (user-installed via the settings page, stylesheet at
  `css_url`), so adding fonts is a row insert, not a code change. `weights` is a JSON array
  driving the weight picker per face (Bebas Neue is `[400]` only).
- **`displays.config_json`** holds the full per-display styling (typography, alignment,
  reference line, background, viewport rect) as one camelCase JSON blob — fields evolve
  without schema changes; defaults live in code (`DisplayConfig.Default`), and Display 1 is
  seeded by `DatabaseInitializer` when the table is empty.
- **`follows_display_id`** is a persistent settings link: a follower always resolves its
  *effective* config to the source display's blob (one level, no chains). Detaching — or
  deleting the source — snapshots the source's config into the follower so it keeps its look.

## The importer-adapter pattern

All content enters through importers that normalize into the same schema:

1. **`ScrollmapperImporter`** (implemented) — reads a scrollmapper/bible_databases SQLite
   file (`{CODE}_books`, `{CODE}_verses`, books in canonical order), validates 66 books,
   imports translation metadata + book names + ~31k verses in one prepared-statement
   transaction. Used by `DatabaseInitializer` to seed bundled translations on first run
   from `data/seed/*.db` (gitignored, downloaded by `scripts/fetch-seed-bibles.cs`).

   That script is not a plain download. Upstream's `formats/sqlite/*.db` currently hold
   seven concatenated copies of each Bible (217,714 verse rows for 31,102 verses; 462 rows
   in `{CODE}_books`), so they fail this importer's 66-book check. The copies disagree —
   against upstream's own CSV export the oldest differs in 152 verses for KJV, 938 for ASV
   and 2,200 for BSB, while the newest matches exactly — so the script keeps the newest row
   per (book, chapter, verse) and verifies every verse against the CSV before writing.
2. **`EasyWorshipImporter`** (implemented, songs) — imports an operator's existing
   EasyWorship song library. The two generations store songs quite differently, so a
   format-specific reader normalizes each into `EasyWorshipSongRow`, and everything after
   that is shared: `RtfStripper` → `LyricsParser` → `SongDraft`, the same splitting the
   editor and .txt import use. Existing titles are skipped, so re-running is safe.

   | Generation | Files | Storage |
   |---|---|---|
   | EasyWorship 2009 and earlier | `Songs.DB` + `Songs.MB` | Paradox table; lyrics are RTF in the blob file |
   | EasyWorship 6/7 | `Songs.db` + `SongWords.db` | Two separate Firebird databases, joined in memory on song id |

   Neither generation is a single file, and 2009's blob file routinely runs to tens of
   megabytes, so the library is read **in place from a path** rather than uploaded.
   `EasyWorshipSource.Locate()` finds it without operator input by reading `AppInstDataDir`
   out of EasyWorship's own profile (`Default.ewp`) and falling back to the default install
   locations; the console only asks for a path when that finds nothing. Format is detected
   by attempting the Paradox parse, whose header validation is strict enough (declared field
   widths must total the record size exactly) that success is a reliable positive.

3. **EasyWorship translation importer** (planned) — the same shape for Bibles: validate →
   map books to canonical numbers → import → appear in the translation dropdown. This is how
   operators bring licensed translations they own without the app redistributing
   copyrighted text.

Seeding is idempotent: existing translation codes are skipped, missing seed files log a
warning and skip (so tests and CI run without the ~14 MB of seed data).

## Bundled translations

| Code | Name | License | Note |
|---|---|---|---|
| KJV | King James Version | public domain* | *scrollmapper module marked GPL (Strong's edition); text verified markup-free |
| ASV | American Standard Version | public domain | |
| BSB | Berean Standard Bible | CC0 (public domain, 2023) | **Substituted for WEB** — scrollmapper's current format doesn't carry the World English Bible; BSB is a modern-English public-domain equivalent |

3 × 31,102 verses ≈ 14 MB database, seeded in seconds on first launch.

## Remote translations — api.bible (migration 005)

NIV, AMP and MSG are copyrighted, so they are **not** bundled. They are fetched per
chapter from [api.bible](https://scripture.api.bible) and cached locally, which keeps the
app's redistribution stance intact: only text the operator's own key retrieves is stored.

```sql
remote_chapters (translation_id, book_number, chapter, fetched_at, expires_at)
```

- **Verses land in the shared `verses` table**, with a `translations` row per remote code
  (`source = 'api.bible'`). Every existing reader — verse lookup, chapter preview, the
  translation re-push — therefore works unchanged, and the translation picker lists them
  for free. `remote_chapters` tracks freshness only.
- **`span_end` handles paraphrases.** The Message fuses verses into blocks (Romans 8's 39
  verses become 11 spans; John 3:16 is part of a 16-18 block). Such a block is stored as
  one row at its first verse with `span_end` set to its last, and the read predicate is an
  overlap test rather than `BETWEEN`, so asking for any verse inside a block returns it.
  Bundled rows keep `span_end` NULL and behave exactly as before.
- **Caching is a sliding 14-day window.** `expires_at` is pushed forward on every
  reference to a chapter, so text in active use is never re-fetched (scripture is
  immutable) and unused text ages out. `DatabaseInitializer` purges expired chapters at
  startup, deleting verses and the tracking row in one transaction so no orphaned verses
  can be served.
- **Prefetch:** resolving a chapter warms chapter ±1 in *all three* remote translations
  (nine chapters), clamped to the book, so switching translation mid-service never waits
  on the network. Only the chapter being displayed is awaited; the rest are background.
  An in-flight guard stops the parser's sticky context from re-requesting on every
  utterance.
- **Degradation:** a cold miss is bounded by `ApiBible:BlockingTimeoutSeconds` (12s — the
  first connection of a process pays DNS + TLS, measured just over 5s; warm requests are
  ~1s) and the connection is warmed at startup so no operator request pays that cost.
  Any failure falls back to whatever is cached rather than throwing, so a dead network
  never stalls the transcription pipeline.
- **Key:** stored in `app_settings` under `apiBible.key` and set from the Settings page, so each
  installation supplies its own and can change it without a restart — the source reads it
  per request rather than pinning it to the `HttpClient`'s default headers. With no key the
  source reports itself unconfigured, every lookup stays local, and the UI refuses to offer
  the online translations. `ApiBible:Key` (from a gitignored `.env`, or any normal
  configuration source) survives only as a one-time seed: `DatabaseInitializer` adopts it
  when the database has no key of its own, so clearing the key in the UI is never undone by
  a stale `.env`.

### `app_settings` (migration 006)

Settings the operator owns rather than the build: a key/value table (`key`, `value`,
`updated_at`) rather than a column per setting, since credentials and feature toggles
arrive one at a time and none warrant a schema change. `SqliteAppSettings` loads the table
once and keeps it in memory — `Get` is on the path of every remote chapter fetch — and
writes update the database and the cache under one lock. Values are plain text: this is a
local, single-operator database on the user's own machine, so encrypting against an
attacker who already has the file would buy nothing. The key is never returned over the
API; `GET /api/settings/api-bible` reports only `configured` and a masked last-four hint.

## Media library (migration 007)

`media_library` backs the Media tab's gallery. It is deliberately **not** `media_assets`
(003), and the split is about ownership of the bytes:

| | `media_assets` (003) | `media_library` (007) |
| --- | --- | --- |
| File location | copied into the WebHost `media/` dir, keyed by GUID | left where the operator put it |
| Row identity | GUID + `file_ext` | absolute `source_path` (unique) |
| Used for | per-display *backgrounds* | projected *content* |

`media_assets` also holds the **default backgrounds** that ship with the app
(`source = 'bundled'`, id `bundled-<filename>`). `BundledBackgroundSeeder` copies them from
the `backgrounds/` folder beside the exe into `media/` at startup, so they are served,
selected and deleted exactly like an upload. The `media.bundledBackgrounds.seeded` app
setting records every default ever registered — the rows alone cannot tell "never
seeded" from "deleted by the operator", and only the first should be (re-)added. See
[Default backgrounds](../README.md#default-backgrounds) for adding one.

```sql
media_library (id, source_path, kind, title, content_type, added_at, sort_order)
```

Because library files are linked rather than copied, a file the operator moves or deletes
leaves a row pointing nowhere. Nothing scans for that in the background: `Exists` is
resolved per read in `SqliteMediaLibraryRepository`, and the console renders a stale row as
an error thumbnail. Deleting a gallery item unlinks the row and never touches the file.

The unique index on `source_path` makes adding idempotent, so re-adding a file — or
re-dropping a folder — updates nothing and duplicates nothing.

Paths only ever originate server-side (`GET /api/media/library/browse`), because a browser
cannot read a file's location from an input or a drop. `GET /api/media/library/{id}/file`
resolves its path from the row alone and never from the query string, so a linked gallery
does not become an arbitrary-file-read endpoint.

Slideshows and video queues are *not* in the database: they are arrangements the operator
builds per service, held in `localStorage` by the console alongside the schedule, and they
reference gallery ids rather than paths.

Because the server knows nothing about queues, queue auto-advance is a relay rather than a
server-side playlist. A queue member is pushed with `mediaLoop: false`, so the display lets
it end and posts `/api/live/media/ended` with the live-item id; the server confirms that id
is what is actually live (rejecting a stale report from a display still showing an old item)
and republishes it as a `mediaended` SSE event. The console advances on that and pushes the
next video. Consecutive pushes carry a positioned reference (`clip (2/5)`) so the same file
twice in a row is not mistaken for a duplicate and suppressed by `LiveState.Show`.

## Future media (approved design, deferred to a later migration)

```sql
songs        (id, title, author, copyright, tags)
song_sections(song_id, position, label, text)            -- Verse 1 / Chorus / ...
triggers     (id, phrase, target_kind, target_id, enabled)
```

`triggers` is the bridge from the parser to media: registered spoken phrases ("as the
deer", "communion video") resolve to a song/image/video through the same cue-detection and
confirm-gate flow used for scripture. Full-text search (FTS5 over verses and lyrics) is a
further additive migration when the feature is needed.

## Runtime pieces

- `SqliteConnectionFactory` — opens connections, creates the file/directory on demand.
- `DatabaseInitializer.Initialize()` — called at startup: migrations → book sync → seeding.
- `SqliteVerseRepository : IVerseRepository` (Core interface) — translation *code* is the
  public identifier; integer PKs stay internal.
- `SqliteStageRepository : IStageRepository` (Core interface) — displays + font registry;
  resolves follow links to effective configs and snapshots on detach/delete.
- `SqliteMediaLibraryRepository : IMediaLibraryRepository` (Core interface) — the linked
  media gallery; resolves `Exists` per read so stale paths surface in the console.
- `TranslationState` (WebHost) — the active translation; default from
  `Data:DefaultTranslation`, switched via `POST /api/translation/{code}`, broadcast to
  clients as a `translation` SSE event.
- Reference SSE events carry `translation` + resolved verse `text`. Chapter-only
  references skip the fetch; lookup failures log and degrade to reference-only display.
