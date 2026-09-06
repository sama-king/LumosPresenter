-- Migration 5: remote (api.bible) translations — NIV, AMP, MSG.
--
-- Remote verses land in the SAME `verses` table as the bundled ones, so every existing
-- reader (verse lookup, chapter preview, translation re-push) works unchanged. What is
-- new is (a) a way to represent paraphrases that fuse verses, and (b) cache expiry.

-- The Message routinely merges verses into one block (Romans 8's 39 verses become 11
-- spans; Psalm 23's 6 become 4). Such a span is stored as ONE row at its first verse
-- with span_end set to its last. NULL — every bundled row — means a plain single verse,
-- so existing data and its behaviour are untouched.
ALTER TABLE verses ADD COLUMN span_end INTEGER NULL;

-- One row per cached (translation, book, chapter). Expiry is a sliding 14-day window:
-- expires_at is pushed forward every time the chapter is referenced, so chapters in
-- active use are never re-fetched (bible text is immutable) and unused ones age out.
CREATE TABLE remote_chapters (
    translation_id INTEGER NOT NULL REFERENCES translations(id) ON DELETE CASCADE,
    book_number    INTEGER NOT NULL REFERENCES books(number),
    chapter        INTEGER NOT NULL,
    fetched_at     TEXT NOT NULL DEFAULT (datetime('now')),
    expires_at     TEXT NOT NULL,
    PRIMARY KEY (translation_id, book_number, chapter)
) WITHOUT ROWID;

-- Drives the startup purge of expired chapters.
CREATE INDEX idx_remote_chapters_expires ON remote_chapters (expires_at);
