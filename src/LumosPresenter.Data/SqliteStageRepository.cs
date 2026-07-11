using System.Text.Json;
using Dapper;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;

namespace LumosPresenter.Data;

/// <summary>
/// Stage configuration over the local SQLite store (Dapper, no EF). Display configs are
/// JSON blobs in displays.config_json; a follower's effective config is resolved here via
/// a join on follows_display_id so callers never see the follower's own (stale) blob.
/// </summary>
public sealed class SqliteStageRepository(SqliteConnectionFactory connectionFactory) : IStageRepository
{
    // camelCase, matching the API wire shape so blobs and DTOs are interchangeable.
    private static readonly JsonSerializerOptions Json = JsonSerializerOptions.Web;

    // Tuple mapping is positional (matches SqliteVerseRepository); a follower's effective
    // config is the source's blob via the self-join COALESCE.
    private const string SelectDisplays = """
        SELECT d.id, d.name, d.sort_order,
               COALESCE(s.config_json, d.config_json),
               d.follows_display_id
        FROM displays d
        LEFT JOIN displays s ON s.id = d.follows_display_id
        """;

    public async Task<IReadOnlyList<StageDisplay>> GetDisplaysAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var rows = await connection.QueryAsync<(int Id, string Name, int SortOrder, string ConfigJson, int? FollowsDisplayId)>(
            new CommandDefinition(
                SelectDisplays + " ORDER BY d.sort_order, d.id",
                cancellationToken: cancellationToken));
        return [.. rows.Select(ToDisplay)];
    }

    public async Task<StageDisplay?> GetDisplayAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var rows = await connection.QueryAsync<(int Id, string Name, int SortOrder, string ConfigJson, int? FollowsDisplayId)>(
            new CommandDefinition(
                SelectDisplays + " WHERE d.id = @Id",
                new { Id = id },
                cancellationToken: cancellationToken));
        var row = rows.SingleOrDefault();
        return row.Name is null ? null : ToDisplay(row);
    }

    public async Task<StageDisplay> CreateDisplayAsync(
        string name, DisplayConfig config, int? followsDisplayId = null, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO displays (name, sort_order, config_json, follows_display_id)
            VALUES (@Name, (SELECT COALESCE(MAX(sort_order) + 1, 0) FROM displays), @ConfigJson, @FollowsDisplayId)
            RETURNING id
            """,
            new { Name = name, ConfigJson = Serialize(config), FollowsDisplayId = followsDisplayId },
            cancellationToken: cancellationToken));
        return (await GetDisplayAsync(id, cancellationToken))!;
    }

    public async Task<StageDisplay?> UpdateConfigAsync(int id, DisplayConfig config, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var affected = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE displays SET config_json = @ConfigJson, updated_at = datetime('now') WHERE id = @Id
            """,
            new { Id = id, ConfigJson = Serialize(config) },
            cancellationToken: cancellationToken));
        return affected == 0 ? null : await GetDisplayAsync(id, cancellationToken);
    }

    public async Task<StageDisplay?> SetFollowsAsync(int id, int? followsDisplayId, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        // Detach keeps the current look: snapshot the effective (source) config as our own.
        var affected = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE displays SET
                config_json = CASE
                    WHEN @FollowsDisplayId IS NULL AND follows_display_id IS NOT NULL
                        THEN COALESCE((SELECT s.config_json FROM displays s WHERE s.id = displays.follows_display_id), config_json)
                    ELSE config_json
                END,
                follows_display_id = @FollowsDisplayId,
                updated_at = datetime('now')
            WHERE id = @Id
            """,
            new { Id = id, FollowsDisplayId = followsDisplayId },
            cancellationToken: cancellationToken));
        return affected == 0 ? null : await GetDisplayAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<int>> GetFollowerIdsAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var ids = await connection.QueryAsync<int>(new CommandDefinition(
            "SELECT id FROM displays WHERE follows_display_id = @Id ORDER BY id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return [.. ids];
    }

    public async Task<bool> DeleteDisplayAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        using var transaction = connection.BeginTransaction();
        // Followers keep the deleted display's look via snapshot before the row goes away.
        await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE displays SET
                config_json = COALESCE((SELECT s.config_json FROM displays s WHERE s.id = @Id), config_json),
                follows_display_id = NULL,
                updated_at = datetime('now')
            WHERE follows_display_id = @Id
            """,
            new { Id = id },
            transaction: transaction,
            cancellationToken: cancellationToken));
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM displays WHERE id = @Id",
            new { Id = id },
            transaction: transaction,
            cancellationToken: cancellationToken));
        transaction.Commit();
        return affected > 0;
    }

    public async Task<int> CountDisplaysAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        return await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM displays",
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyList<FontInfo>> GetFontsAsync(bool enabledOnly = true, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var rows = await connection.QueryAsync<(string Slug, string Name, string CssFamily, string Source, string? CssUrl, string Weights, long Enabled, int SortOrder)>(
            new CommandDefinition(
                "SELECT slug, name, css_family, source, css_url, weights, enabled, sort_order FROM fonts"
                + (enabledOnly ? " WHERE enabled = 1" : string.Empty)
                + " ORDER BY sort_order, name",
                cancellationToken: cancellationToken));
        return [.. rows.Select(r => new FontInfo(
            r.Slug, r.Name, r.CssFamily, r.Source, r.CssUrl,
            JsonSerializer.Deserialize<int[]>(r.Weights) ?? [400, 700],
            r.Enabled != 0, r.SortOrder))];
    }

    public async Task<IReadOnlyList<MediaAsset>> GetMediaAssetsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var rows = await connection.QueryAsync<(string Id, string Kind, string Title, string FileExt, string ContentType, string Source, int SortOrder)>(
            new CommandDefinition(
                "SELECT id, kind, title, file_ext, content_type, source, sort_order FROM media_assets ORDER BY sort_order, created_at DESC",
                cancellationToken: cancellationToken));
        return [.. rows.Select(r => new MediaAsset(r.Id, r.Kind, r.Title, r.FileExt, r.ContentType, r.Source, r.SortOrder))];
    }

    public async Task<MediaAsset?> GetMediaAssetAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var rows = await connection.QueryAsync<(string Id, string Kind, string Title, string FileExt, string ContentType, string Source, int SortOrder)>(
            new CommandDefinition(
                "SELECT id, kind, title, file_ext, content_type, source, sort_order FROM media_assets WHERE id = @Id",
                new { Id = id },
                cancellationToken: cancellationToken));
        var row = rows.SingleOrDefault();
        return row.Id is null ? null : new MediaAsset(row.Id, row.Kind, row.Title, row.FileExt, row.ContentType, row.Source, row.SortOrder);
    }

    public async Task<MediaAsset> AddMediaAssetAsync(MediaAsset asset, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO media_assets (id, kind, title, file_ext, content_type, source, sort_order)
            VALUES (@Id, @Kind, @Title, @FileExt, @ContentType, @Source,
                    (SELECT COALESCE(MAX(sort_order) + 1, 0) FROM media_assets))
            """,
            new { asset.Id, asset.Kind, asset.Title, asset.FileExt, asset.ContentType, asset.Source },
            cancellationToken: cancellationToken));
        return (await GetMediaAssetAsync(asset.Id, cancellationToken))!;
    }

    public async Task<bool> DeleteMediaAssetAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM media_assets WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return affected > 0;
    }

    internal static string Serialize(DisplayConfig config) => JsonSerializer.Serialize(config, Json);

    private static StageDisplay ToDisplay((int Id, string Name, int SortOrder, string ConfigJson, int? FollowsDisplayId) row) => new(
        row.Id, row.Name, row.SortOrder,
        (JsonSerializer.Deserialize<DisplayConfig>(row.ConfigJson, Json) ?? DisplayConfig.Default).Normalized(),
        row.FollowsDisplayId);
}
