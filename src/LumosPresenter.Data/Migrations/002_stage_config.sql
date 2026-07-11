-- Migration 2: stage configuration — font registry + per-display styling.
-- Displays style the single global live feed; a display with follows_display_id
-- set permanently mirrors the referenced display's settings (one level, no chains).

CREATE TABLE fonts (
    id          INTEGER PRIMARY KEY,
    slug        TEXT NOT NULL UNIQUE,
    name        TEXT NOT NULL,
    css_family  TEXT NOT NULL,
    source      TEXT NOT NULL DEFAULT 'bundled',  -- 'bundled' | 'file' (future user-installed)
    css_url     TEXT,                             -- stylesheet URL for source='file' (future)
    weights     TEXT NOT NULL DEFAULT '[400,500,600,700,800]',  -- JSON array of selectable weights
    enabled     INTEGER NOT NULL DEFAULT 1,
    sort_order  INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE displays (
    id                 INTEGER PRIMARY KEY,
    name               TEXT NOT NULL,
    sort_order         INTEGER NOT NULL DEFAULT 0,
    config_json        TEXT NOT NULL,
    follows_display_id INTEGER REFERENCES displays(id),  -- persistent settings link; NULL = custom
    created_at         TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at         TEXT NOT NULL DEFAULT (datetime('now'))
);

INSERT INTO fonts (slug, name, css_family, weights, sort_order) VALUES
  ('hanken-grotesk', 'Hanken Grotesk', '''Hanken Grotesk Variable'', sans-serif', '[400,500,600,700,800]', 0),
  ('inter',          'Inter',          '''Inter Variable'', sans-serif',          '[400,500,600,700,800]', 1),
  ('montserrat',     'Montserrat',     '''Montserrat Variable'', sans-serif',     '[400,500,600,700,800]', 2),
  ('eb-garamond',    'EB Garamond',    '''EB Garamond Variable'', serif',         '[400,500,600,700,800]', 3),
  ('lora',           'Lora',           '''Lora Variable'', serif',                '[400,500,600,700]',     4),
  ('source-serif-4', 'Source Serif 4', '''Source Serif 4 Variable'', serif',      '[400,500,600,700,800]', 5),
  ('bebas-neue',     'Bebas Neue',     '''Bebas Neue'', sans-serif',              '[400]',                 6),
  ('jetbrains-mono', 'JetBrains Mono', '''JetBrains Mono Variable'', monospace',  '[400,500,700]',         7);
