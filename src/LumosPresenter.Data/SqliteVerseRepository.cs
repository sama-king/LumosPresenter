using Dapper;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using LumosPresenter.Core.Parsing;

namespace LumosPresenter.Data;

/// <summary>Read-only verse lookup over the local SQLite store (Dapper, no EF).</summary>
public sealed class SqliteVerseRepository(SqliteConnectionFactory connectionFactory) : IVerseRepository
{
    public async Task<IReadOnlyList<Translation>> GetTranslationsAsync(CancellationToken cancellationToken = default)
    {
        using var connection = connectionFactory.Open();
        var rows = await connection.QueryAsync<(string Code, string Name, string Language, string Source)>(
            new CommandDefinition(
                "SELECT code, name, language, source FROM translations ORDER BY code",
                cancellationToken: cancellationToken));
        return [.. rows.Select(r => new Translation(r.Code, r.Name, r.Language, r.Source))];
    }

    public async Task<IReadOnlyList<Verse>> GetVersesAsync(
        string translationId, BibleReference reference, CancellationToken cancellationToken = default)
    {
        var book = BookCatalog.Books.FirstOrDefault(b => b.Name == reference.Book);
        if (book is null)
        {
            return [];
        }

        // Chapter-only references resolve the whole chapter; verse references resolve the range.
        var verseStart = reference.VerseStart ?? 1;
        var verseEnd = reference.VerseStart is null
            ? int.MaxValue
            : reference.VerseEnd ?? reference.VerseStart.Value;

        using var connection = connectionFactory.Open();
        // Overlap test rather than a plain BETWEEN: a paraphrase (MSG) stores a fused
        // block as one row spanning verse..span_end, and asking for any verse inside
        // that span must return it. span_end is NULL for ordinary single verses.
        var rows = await connection.QueryAsync<(int Verse, string Text)>(
            new CommandDefinition("""
                SELECT v.verse, v.text
                FROM verses v
                JOIN translations t ON t.id = v.translation_id
                WHERE t.code = @Code
                  AND v.book_number = @BookNumber
                  AND v.chapter = @Chapter
                  AND v.verse <= @VerseEnd
                  AND COALESCE(v.span_end, v.verse) >= @VerseStart
                ORDER BY v.verse
                """,
                new
                {
                    Code = translationId,
                    BookNumber = book.Number,
                    reference.Chapter,
                    VerseStart = verseStart,
                    VerseEnd = verseEnd,
                },
                cancellationToken: cancellationToken));

        return [.. rows.Select(r => new Verse(translationId, book.Name, reference.Chapter, r.Verse, r.Text))];
    }
}
