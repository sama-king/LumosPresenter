-- Migration 1: bible schema + display history.
-- Media tables (songs, media_assets, triggers) arrive as a later additive migration.

CREATE TABLE translations (
    id               INTEGER PRIMARY KEY,
    code             TEXT NOT NULL UNIQUE,
    name             TEXT NOT NULL,
    language         TEXT NOT NULL,
    source           TEXT NOT NULL,             -- 'bundled' | 'easyworship'
    license          TEXT,
    imported_at      TEXT,
    content_version  INTEGER NOT NULL DEFAULT 1
);

CREATE TABLE books (
    number         INTEGER PRIMARY KEY,          -- 1..66 canonical order
    name           TEXT NOT NULL,                -- matches the parser's BookCatalog
    chapter_count  INTEGER NOT NULL
);

CREATE TABLE book_names (
    translation_id INTEGER NOT NULL REFERENCES translations(id) ON DELETE CASCADE,
    book_number    INTEGER NOT NULL REFERENCES books(number),
    name           TEXT NOT NULL,
    PRIMARY KEY (translation_id, book_number)
);

CREATE TABLE verses (
    translation_id INTEGER NOT NULL REFERENCES translations(id) ON DELETE CASCADE,
    book_number    INTEGER NOT NULL REFERENCES books(number),
    chapter        INTEGER NOT NULL,
    verse          INTEGER NOT NULL,
    text           TEXT NOT NULL,
    PRIMARY KEY (translation_id, book_number, chapter, verse)
) WITHOUT ROWID;

CREATE TABLE display_history (
    id        INTEGER PRIMARY KEY,
    shown_at  TEXT NOT NULL DEFAULT (datetime('now')),
    kind      TEXT NOT NULL,                     -- 'verse' now; 'song' | 'image' | 'video' later
    reference TEXT NOT NULL,
    detail    TEXT
);
