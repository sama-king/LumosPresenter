-- Migration 6: operator-editable settings that belong to the installation, not the build.
--
-- The api.bible key was previously supplied through a gitignored .env at the repository
-- root, which only works for a developer running from a checkout. Users install a built
-- app, so the key has to be something they can enter and change from the UI — which means
-- it lives with their data, in the database.
--
-- A generic key/value table rather than a column per setting: settings of this kind
-- (credentials, feature toggles) arrive one at a time and none of them warrant a schema
-- change. Values are stored as plain text; this is a local, single-operator database on
-- the user's own machine, so encrypting against an attacker who already has the file
-- would buy nothing.
CREATE TABLE app_settings (
    key        TEXT PRIMARY KEY,
    value      TEXT NOT NULL,
    updated_at TEXT NOT NULL DEFAULT (datetime('now'))
) WITHOUT ROWID;
