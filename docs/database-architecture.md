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
verses       (translation_id, book_number, chapter, verse, text)
             -- composite PK, WITHOUT ROWID: range lookups ride the PK index
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
   from `data/seed/*.db` (gitignored, downloaded).
2. **EasyWorship importer** (planned) — same shape: validate → map books to canonical
   numbers → import → appear in the translation dropdown. This is how operators bring
   licensed translations they own without the app redistributing copyrighted text.

Seeding is idempotent: existing translation codes are skipped, missing seed files log a
warning and skip (so tests and CI run without the ~14 MB of seed data).

## Bundled translations

| Code | Name | License | Note |
|---|---|---|---|
| KJV | King James Version | public domain* | *scrollmapper module marked GPL (Strong's edition); text verified markup-free |
| ASV | American Standard Version | public domain | |
| BSB | Berean Standard Bible | CC0 (public domain, 2023) | **Substituted for WEB** — scrollmapper's current format doesn't carry the World English Bible; BSB is a modern-English public-domain equivalent |

3 × 31,102 verses ≈ 14 MB database, seeded in seconds on first launch.

## Future media (approved design, deferred to a later migration)

```sql
songs        (id, title, author, copyright, tags)
song_sections(song_id, position, label, text)            -- Verse 1 / Chorus / ...
media_assets (id, kind, path, title, duration_ms, tags)  -- files on disk, metadata here
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
- `TranslationState` (WebHost) — the active translation; default from
  `Data:DefaultTranslation`, switched via `POST /api/translation/{code}`, broadcast to
  clients as a `translation` SSE event.
- Reference SSE events carry `translation` + resolved verse `text`. Chapter-only
  references skip the fetch; lookup failures log and degrade to reference-only display.
