using Dapper;
using LumosPresenter.Core.Abstractions;

namespace LumosPresenter.Data;

/// <summary>
/// Settings backed by the app_settings table. The whole table is a handful of rows, so it
/// is loaded once and kept in memory: <see cref="IAppSettings.Get"/> is on the path of
/// every remote chapter fetch and must not hit SQLite each time. Writes go to the database
/// and update the cache under the same lock, so a key saved from the UI takes effect
/// immediately — no restart, unlike the configuration it replaces.
/// </summary>
public sealed class SqliteAppSettings(SqliteConnectionFactory connectionFactory) : IAppSettings
{
    private readonly Lock _gate = new();
    private Dictionary<string, string>? _cache;

    public string? Get(string key)
    {
        lock (_gate)
        {
            _cache ??= Load();
            return _cache.TryGetValue(key, out var value) ? value : null;
        }
    }

    public void Set(string key, string? value)
    {
        var trimmed = value?.Trim();
        lock (_gate)
        {
            using var connection = connectionFactory.Open();
            if (string.IsNullOrEmpty(trimmed))
            {
                connection.Execute("DELETE FROM app_settings WHERE key = @key", new { key });
            }
            else
            {
                connection.Execute("""
                    INSERT INTO app_settings (key, value, updated_at)
                    VALUES (@key, @value, datetime('now'))
                    ON CONFLICT(key) DO UPDATE SET value = @value, updated_at = datetime('now')
                    """, new { key, value = trimmed });
            }

            _cache ??= Load();
            if (string.IsNullOrEmpty(trimmed))
            {
                _cache.Remove(key);
            }
            else
            {
                _cache[key] = trimmed;
            }
        }
    }

    private Dictionary<string, string> Load()
    {
        using var connection = connectionFactory.Open();
        return connection.Query<(string Key, string Value)>("SELECT key, value FROM app_settings")
            .ToDictionary(row => row.Key, row => row.Value);
    }
}
