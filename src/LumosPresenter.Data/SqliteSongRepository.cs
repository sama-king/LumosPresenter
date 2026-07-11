using Dapper;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;

namespace LumosPresenter.Data;

/// <summary>
/// Song library over the local SQLite store (Dapper, no EF). Sections live in their own
/// table keyed by (song_id, position); a save replaces them all, mirroring the
/// delete-then-insert idempotency of the translation importers.
/// </summary>
public sealed class SqliteSongRepository(SqliteConnectionFactory connectionFactory) : ISongRepository
{
    public async Task<IReadOnlyList<SongSummary>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var like = string.IsNullOrWhiteSpace(query) ? null : $"%{query.Trim()}%";
        var rows = await connection.QueryAsync<(int Id, string Title, string? Author)>(
            new CommandDefinition("""
                SELECT id, title, author FROM songs
                WHERE @Like IS NULL OR title LIKE @Like COLLATE NOCASE
                ORDER BY title COLLATE NOCASE, id
                """,
                new { Like = like },
                cancellationToken: cancellationToken));
        return [.. rows.Select(r => new SongSummary(r.Id, r.Title, r.Author))];
    }

    public async Task<Song?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var head = (await connection.QueryAsync<(int Id, string Title, string? Author, string? Copyright)>(
            new CommandDefinition(
                "SELECT id, title, author, copyright FROM songs WHERE id = @Id",
                new { Id = id },
                cancellationToken: cancellationToken))).SingleOrDefault();
        if (head.Title is null)
        {
            return null;
        }
        var sections = await connection.QueryAsync<(int Position, string? Label, string Text)>(
            new CommandDefinition(
                "SELECT position, label, text FROM song_sections WHERE song_id = @Id ORDER BY position",
                new { Id = id },
                cancellationToken: cancellationToken));
        return new Song(head.Id, head.Title, head.Author, head.Copyright,
            [.. sections.Select(s => new SongSection(s.Position, s.Label, s.Text))]);
    }

    public async Task<Song> CreateAsync(SongDraft draft, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        using var transaction = connection.BeginTransaction();
        var id = await connection.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO songs (title, author, copyright, source)
            VALUES (@Title, @Author, @Copyright, @Source)
            RETURNING id
            """,
            new { draft.Title, draft.Author, draft.Copyright, draft.Source },
            transaction: transaction,
            cancellationToken: cancellationToken));
        await InsertSectionsAsync(connection, transaction, id, draft.Sections, cancellationToken);
        transaction.Commit();
        return (await GetAsync(id, cancellationToken))!;
    }

    public async Task<Song?> UpdateAsync(int id, SongDraft draft, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        using var transaction = connection.BeginTransaction();
        var affected = await connection.ExecuteAsync(new CommandDefinition("""
            UPDATE songs SET title = @Title, author = @Author, copyright = @Copyright,
                             updated_at = datetime('now')
            WHERE id = @Id
            """,
            new { Id = id, draft.Title, draft.Author, draft.Copyright },
            transaction: transaction,
            cancellationToken: cancellationToken));
        if (affected == 0)
        {
            return null;
        }
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM song_sections WHERE song_id = @Id",
            new { Id = id },
            transaction: transaction,
            cancellationToken: cancellationToken));
        await InsertSectionsAsync(connection, transaction, id, draft.Sections, cancellationToken);
        transaction.Commit();
        return await GetAsync(id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        using var transaction = connection.BeginTransaction();
        // Explicit child delete: don't rely on ON DELETE CASCADE firing across configs.
        await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM song_sections WHERE song_id = @Id",
            new { Id = id },
            transaction: transaction,
            cancellationToken: cancellationToken));
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            "DELETE FROM songs WHERE id = @Id",
            new { Id = id },
            transaction: transaction,
            cancellationToken: cancellationToken));
        transaction.Commit();
        return affected > 0;
    }

    private static async Task InsertSectionsAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        int songId,
        IReadOnlyList<SongSection> sections,
        CancellationToken cancellationToken)
    {
        // Re-index positions so they are always contiguous 0..n-1 regardless of input.
        for (var position = 0; position < sections.Count; position++)
        {
            var section = sections[position];
            await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO song_sections (song_id, position, label, text)
                VALUES (@SongId, @Position, @Label, @Text)
                """,
                new { SongId = songId, Position = position, section.Label, section.Text },
                transaction: transaction,
                cancellationToken: cancellationToken));
        }
    }
}
