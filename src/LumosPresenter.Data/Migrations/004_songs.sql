-- Migration 4: song library. Sections are the projection slides: one row per
-- blank-line-separated lyrics block, replace-all on edit. Title search is LIKE
-- for now; FTS5 over lyrics is a future additive migration (see docs).

CREATE TABLE songs (
    id         INTEGER PRIMARY KEY AUTOINCREMENT,
    title      TEXT NOT NULL,
    author     TEXT NULL,
    copyright  TEXT NULL,
    tags       TEXT NULL,                              -- reserved (approved docs schema); unused by the UI yet
    source     TEXT NOT NULL DEFAULT 'editor',         -- 'editor' | 'txt' | 'easyworship'
    created_at TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_songs_title ON songs (title COLLATE NOCASE);

CREATE TABLE song_sections (
    song_id  INTEGER NOT NULL REFERENCES songs(id) ON DELETE CASCADE,
    position INTEGER NOT NULL,                         -- 0-based slide order
    label    TEXT NULL,                                -- 'Verse 1' / 'Chorus' / null
    text     TEXT NOT NULL,
    PRIMARY KEY (song_id, position)
);
