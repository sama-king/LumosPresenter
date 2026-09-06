using Dapper;
using LumosPresenter.Core.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Data.Remote;

/// <summary>
/// Persistence for fetched chapters. Verses land in the shared `verses` table so every
/// existing reader sees them; `remote_chapters` tracks only freshness. Expiry is a
/// sliding window — any reference to a chapter pushes its expiry out again — so text in
/// active use is never re-fetched and unused text ages out.
/// </summary>
public sealed class RemoteChapterCache(
    SqliteConnectionFactory connectionFactory,
    IOptions<ApiBibleOptions> options,
    ILogger<RemoteChapterCache> logger)
{
    private readonly ApiBibleOptions _options = options.Value;

    /// <summary>True when this chapter is cached and unexpired.</summary>
    public bool IsFresh(string translationId, int bookNumber, int chapter)
    {
        using var connection = connectionFactory.Open();
        return connection.ExecuteScalar<int>("""
            SELECT COUNT(*)
            FROM remote_chapters rc
            JOIN translations t ON t.id = rc.translation_id
            WHERE t.code = @Code AND rc.book_number = @BookNumber AND rc.chapter = @Chapter
              AND rc.expires_at > datetime('now')
            """,
            new { Code = translationId, BookNumber = bookNumber, Chapter = chapter }) > 0;
    }

    /// <summary>
    /// Pushes a cached chapter's expiry back to the full window. Called whenever a
    /// chapter is referenced, which is what makes the 14 days sliding rather than fixed.
    /// </summary>
    public void Touch(string translationId, int bookNumber, int chapter)
    {
        using var connection = connectionFactory.Open();
        connection.Execute($"""
            UPDATE remote_chapters
            SET expires_at = datetime('now', '+{_options.CacheDays} days')
            WHERE translation_id = (SELECT id FROM translations WHERE code = @Code)
              AND book_number = @BookNumber AND chapter = @Chapter
            """,
            new { Code = translationId, BookNumber = bookNumber, Chapter = chapter });
    }

    /// <summary>
    /// Renews a whole chapter range across every given translation in one statement. The
    /// prefetch window touches nine chapters per lookup and this sits on the transcription
    /// hot path, so it must not be nine separate connections.
    /// </summary>
    public void TouchRange(IEnumerable<string> translationIds, int bookNumber, int firstChapter, int lastChapter)
    {
        using var connection = connectionFactory.Open();
        connection.Execute($"""
            UPDATE remote_chapters
            SET expires_at = datetime('now', '+{_options.CacheDays} days')
            WHERE translation_id IN (SELECT id FROM translations WHERE code IN @Codes)
              AND book_number = @BookNumber
              AND chapter BETWEEN @First AND @Last
            """,
            new
            {
                Codes = translationIds.ToArray(),
                BookNumber = bookNumber,
                First = firstChapter,
                Last = lastChapter,
            });
    }

    /// <summary>Which of the given chapters are already cached and unexpired.</summary>
    public HashSet<int> FreshChapters(string translationId, int bookNumber, int firstChapter, int lastChapter)
    {
        using var connection = connectionFactory.Open();
        return [.. connection.Query<int>("""
            SELECT rc.chapter
            FROM remote_chapters rc
            JOIN translations t ON t.id = rc.translation_id
            WHERE t.code = @Code AND rc.book_number = @BookNumber
              AND rc.chapter BETWEEN @First AND @Last
              AND rc.expires_at > datetime('now')
            """,
            new { Code = translationId, BookNumber = bookNumber, First = firstChapter, Last = lastChapter })];
    }

    /// <summary>
    /// Stores a fetched chapter, replacing any previous copy, and starts its 14-day
    /// window. Verse rows and the tracking row are written in one transaction so a
    /// failure can never leave verses that no expiry row governs.
    /// </summary>
    public void Save(RemoteChapter chapter)
    {
        using var connection = connectionFactory.Open();
        using var transaction = connection.BeginTransaction();

        var translationId = connection.ExecuteScalar<long?>(
            "SELECT id FROM translations WHERE code = @Code",
            new { Code = chapter.TranslationId }, transaction);
        if (translationId is null)
        {
            logger.LogWarning("No translations row for {Code}; chapter not cached", chapter.TranslationId);
            return;
        }

        connection.Execute("""
            DELETE FROM verses
            WHERE translation_id = @TranslationId AND book_number = @BookNumber AND chapter = @Chapter
            """,
            new { TranslationId = translationId, chapter.BookNumber, chapter.Chapter }, transaction);

        connection.Execute("""
            INSERT INTO verses (translation_id, book_number, chapter, verse, text, span_end)
            VALUES (@TranslationId, @BookNumber, @Chapter, @Verse, @Text, @SpanEnd)
            """,
            chapter.Verses.Select(v => new
            {
                TranslationId = translationId,
                chapter.BookNumber,
                chapter.Chapter,
                v.Verse,
                v.Text,
                // NULL for an ordinary verse; only fused spans (MSG) carry an end.
                SpanEnd = v.SpanEnd > v.Verse ? v.SpanEnd : (int?)null,
            }), transaction);

        connection.Execute($"""
            INSERT INTO remote_chapters (translation_id, book_number, chapter, fetched_at, expires_at)
            VALUES (@TranslationId, @BookNumber, @Chapter, datetime('now'), datetime('now', '+{_options.CacheDays} days'))
            ON CONFLICT(translation_id, book_number, chapter) DO UPDATE
              SET fetched_at = excluded.fetched_at, expires_at = excluded.expires_at
            """,
            new { TranslationId = translationId, chapter.BookNumber, chapter.Chapter }, transaction);

        // The provider's copyright line is the licence of record for a remote translation.
        if (!string.IsNullOrWhiteSpace(chapter.Copyright))
        {
            connection.Execute(
                "UPDATE translations SET license = @License WHERE id = @Id AND (license IS NULL OR license = '')",
                new { License = chapter.Copyright, Id = translationId }, transaction);
        }

        transaction.Commit();
    }

    /// <summary>
    /// Deletes chapters whose window has elapsed, verses and tracking row together. Run
    /// at startup; a chapter still in use will have been touched and so survives.
    /// </summary>
    public int PurgeExpired()
    {
        using var connection = connectionFactory.Open();
        using var transaction = connection.BeginTransaction();

        var expired = connection.Query<(long TranslationId, int BookNumber, int Chapter)>(
            "SELECT translation_id, book_number, chapter FROM remote_chapters WHERE expires_at <= datetime('now')",
            transaction: transaction).ToList();
        if (expired.Count == 0)
        {
            return 0;
        }

        foreach (var (translationId, bookNumber, chapter) in expired)
        {
            var key = new { TranslationId = translationId, BookNumber = bookNumber, Chapter = chapter };
            connection.Execute("""
                DELETE FROM verses
                WHERE translation_id = @TranslationId AND book_number = @BookNumber AND chapter = @Chapter
                """, key, transaction);
            connection.Execute("""
                DELETE FROM remote_chapters
                WHERE translation_id = @TranslationId AND book_number = @BookNumber AND chapter = @Chapter
                """, key, transaction);
        }

        transaction.Commit();
        logger.LogInformation("Purged {Count} expired remote chapter(s)", expired.Count);
        return expired.Count;
    }
}
