using Dapper;
using LumosPresenter.Core.Parsing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace LumosPresenter.Data.Seeding;

/// <summary>A translation to import: its scrollmapper file plus display metadata.</summary>
public sealed record ScrollmapperSource(string Code, string Name, string Language, string License, string FilePath);

/// <summary>
/// Imports a scrollmapper/bible_databases SQLite file (tables {CODE}_books and
/// {CODE}_verses, books in canonical order) into the normalized schema. The first of the
/// importer adapters — the EasyWorship importer follows the same shape.
/// </summary>
public sealed class ScrollmapperImporter(SqliteConnectionFactory connectionFactory, ILogger<ScrollmapperImporter> logger)
{
    public void Import(ScrollmapperSource source)
    {
        using var sourceConnection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = source.FilePath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());
        sourceConnection.Open();

        // Validate shape before touching the target: 66 books in canonical order.
        var bookCount = sourceConnection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {source.Code}_books");
        if (bookCount != BookCatalog.Books.Count)
        {
            throw new InvalidDataException(
                $"{source.FilePath}: expected {BookCatalog.Books.Count} books, found {bookCount}.");
        }

        var sourceBooks = sourceConnection
            .Query<(int Id, string Name)>($"SELECT id, name FROM {source.Code}_books ORDER BY id")
            .ToList();

        using var target = connectionFactory.Open();
        using var transaction = target.BeginTransaction();

        target.Execute("DELETE FROM translations WHERE code = @Code", new { source.Code }, transaction);
        var translationId = target.ExecuteScalar<long>("""
            INSERT INTO translations (code, name, language, source, license, imported_at)
            VALUES (@Code, @Name, @Language, 'bundled', @License, datetime('now'))
            RETURNING id
            """, source, transaction);

        foreach (var (id, name) in sourceBooks)
        {
            target.Execute(
                "INSERT INTO book_names (translation_id, book_number, name) VALUES (@translationId, @id, @name)",
                new { translationId, id, name = name.Trim() }, transaction);
        }

        // 31k rows: a prepared command inside one transaction keeps this fast.
        using var insert = target.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO verses (translation_id, book_number, chapter, verse, text)
            VALUES ($t, $b, $c, $v, $x)
            """;
        var pTranslation = insert.Parameters.Add("$t", SqliteType.Integer);
        var pBook = insert.Parameters.Add("$b", SqliteType.Integer);
        var pChapter = insert.Parameters.Add("$c", SqliteType.Integer);
        var pVerse = insert.Parameters.Add("$v", SqliteType.Integer);
        var pText = insert.Parameters.Add("$x", SqliteType.Text);
        pTranslation.Value = translationId;

        var count = 0;
        using var reader = new SqliteCommand(
            $"SELECT book_id, chapter, verse, text FROM {source.Code}_verses", sourceConnection).ExecuteReader();
        while (reader.Read())
        {
            pBook.Value = reader.GetInt32(0);
            pChapter.Value = reader.GetInt32(1);
            pVerse.Value = reader.GetInt32(2);
            pText.Value = reader.GetString(3).Trim();
            insert.ExecuteNonQuery();
            count++;
        }

        transaction.Commit();
        logger.LogInformation("Imported {Code}: {Count} verses from {Path}", source.Code, count, source.FilePath);
    }
}
