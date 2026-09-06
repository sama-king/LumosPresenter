using Dapper;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;

namespace LumosPresenter.Data;

/// <summary>
/// Media gallery over the local SQLite store (Dapper, no EF). Rows hold absolute source paths;
/// nothing is copied. File.Exists is evaluated on read so the console can grey out items whose
/// file has moved without the gallery needing a background scanner.
/// </summary>
public sealed class SqliteMediaLibraryRepository(SqliteConnectionFactory connectionFactory)
    : IMediaLibraryRepository
{
    // SQLite reports INTEGER columns as Int64, so sort_order is read as long and narrowed
    // here rather than being bound straight onto the record's int field.
    private sealed record Row(
        string Id, string SourcePath, string Kind, string Title, string ContentType, long SortOrder);

    private static MediaLibraryItem ToItem(Row row) => new(
        row.Id, row.SourcePath, row.Kind, row.Title, row.ContentType, (int)row.SortOrder,
        File.Exists(row.SourcePath));

    public async Task<IReadOnlyList<MediaLibraryItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var rows = await connection.QueryAsync<Row>(new CommandDefinition("""
            SELECT id AS Id, source_path AS SourcePath, kind AS Kind, title AS Title,
                   content_type AS ContentType, sort_order AS SortOrder
            FROM media_library
            ORDER BY sort_order, added_at DESC, id
            """,
            cancellationToken: cancellationToken));
        return [.. rows.Select(ToItem)];
    }

    public async Task<MediaLibraryItem?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var row = await connection.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
            SELECT id AS Id, source_path AS SourcePath, kind AS Kind, title AS Title,
                   content_type AS ContentType, sort_order AS SortOrder
            FROM media_library WHERE id = @Id
            """,
            new { Id = id },
            cancellationToken: cancellationToken));
        return row is null ? null : ToItem(row);
    }

    public async Task<MediaLibraryItem> AddAsync(
        string sourcePath, string kind, string title, string contentType,
        CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        // ON CONFLICT DO NOTHING + RETURNING yields no row when the path is already linked,
        // so fall back to reading the existing one — the add stays idempotent per path.
        var id = Guid.NewGuid().ToString("N");
        var inserted = await connection.QuerySingleOrDefaultAsync<string>(new CommandDefinition("""
            INSERT INTO media_library (id, source_path, kind, title, content_type)
            VALUES (@Id, @SourcePath, @Kind, @Title, @ContentType)
            ON CONFLICT (source_path) DO NOTHING
            RETURNING id
            """,
            new { Id = id, SourcePath = sourcePath, Kind = kind, Title = title, ContentType = contentType },
            cancellationToken: cancellationToken));

        var row = await connection.QuerySingleAsync<Row>(new CommandDefinition("""
            SELECT id AS Id, source_path AS SourcePath, kind AS Kind, title AS Title,
                   content_type AS ContentType, sort_order AS SortOrder
            FROM media_library WHERE source_path = @SourcePath
            """,
            new { SourcePath = sourcePath },
            cancellationToken: cancellationToken));
        _ = inserted;
        return ToItem(row);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM media_library WHERE id = @Id",
            new { Id = id },
            cancellationToken: cancellationToken));
        return affected > 0;
    }
}
