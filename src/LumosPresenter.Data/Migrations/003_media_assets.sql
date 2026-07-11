-- Migration 3: media asset registry — uploaded images and looping videos used as
-- per-display backgrounds. Mirrors the fonts registry (002): metadata here, the file
-- itself lives on disk under the WebHost 'media/' directory, keyed by id.
-- A display references an asset by id inside its config_json blob (background.assetId);
-- no displays-table change is needed.

CREATE TABLE media_assets (
    id           TEXT PRIMARY KEY,               -- Guid "N"; also the on-disk filename stem
    kind         TEXT NOT NULL,                  -- 'image' | 'motion'
    title        TEXT NOT NULL,
    file_ext     TEXT NOT NULL,                  -- original extension incl. leading dot (e.g. '.mp4')
    content_type TEXT NOT NULL,
    source       TEXT NOT NULL DEFAULT 'file',   -- 'bundled' | 'file' (mirrors fonts)
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    sort_order   INTEGER NOT NULL DEFAULT 0
);
