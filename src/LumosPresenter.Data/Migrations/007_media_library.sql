-- Migration 7: media library — images and videos the operator projects as content.
-- Deliberately NOT media_assets (003): that registry holds *background* files copied into
-- the WebHost media/ dir and keyed by GUID. Library items are LINKED, never copied: the row
-- holds the absolute source path on the operator's machine, so moving or deleting the file
-- on disk leaves a stale row that the console renders as an error thumbnail.

CREATE TABLE media_library (
    id           TEXT PRIMARY KEY,               -- Guid "N"
    source_path  TEXT NOT NULL,                  -- absolute path on this machine; the only path source
    kind         TEXT NOT NULL,                  -- 'image' | 'video'
    title        TEXT NOT NULL,                  -- file name (sans extension) by default
    content_type TEXT NOT NULL,
    added_at     TEXT NOT NULL DEFAULT (datetime('now')),
    sort_order   INTEGER NOT NULL DEFAULT 0
);

-- Re-adding the same file is a no-op rather than a duplicate tile in the gallery.
CREATE UNIQUE INDEX idx_media_library_path ON media_library (source_path);
