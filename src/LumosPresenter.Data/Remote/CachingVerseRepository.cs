using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using LumosPresenter.Core.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Data.Remote;

/// <summary>
/// Wraps the local verse repository so remote translations (NIV/AMP/MSG) behave exactly
/// like bundled ones. On a lookup it serves cache when fresh; otherwise it fetches the
/// chapter, then — in the background — prefetches that chapter ±1 in ALL remote
/// translations, so switching translation mid-service never waits on the network.
///
/// Every failure degrades to whatever is cached: a dead network must never stall the
/// transcription pipeline or take the displays down.
/// </summary>
public sealed class CachingVerseRepository(
    SqliteVerseRepository inner,
    IRemoteScriptureSource remote,
    RemoteChapterCache cache,
    IOptions<ApiBibleOptions> options,
    ILogger<CachingVerseRepository> logger) : IVerseRepository
{
    private readonly ApiBibleOptions _options = options.Value;

    // Guards against the parser's sticky context re-resolving the same chapter on every
    // utterance: chapter ±1 across three translations is nine chapters per resolution.
    private readonly HashSet<(string, int, int)> _inFlight = [];

    public Task<IReadOnlyList<Translation>> GetTranslationsAsync(CancellationToken cancellationToken = default) =>
        inner.GetTranslationsAsync(cancellationToken);

    /// <summary>
    /// Opens the connection to the provider in the background at startup. The first
    /// request of the process pays DNS and the TLS handshake — several seconds, enough to
    /// exceed the blocking ceiling — so we spend it before an operator is waiting on a
    /// verse. Failure is irrelevant: this only warms the pool.
    /// </summary>
    public void WarmUp()
    {
        if (!remote.IsConfigured || remote.Translations.Count == 0)
        {
            return;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                // Genesis 1 — cached like any other chapter, so the work is not wasted.
                await FetchAndSaveAsync(remote.Translations[0].Id, 1, 1, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "api.bible warm-up failed (harmless)");
            }
        });
    }

    public async Task<IReadOnlyList<Verse>> GetVersesAsync(
        string translationId, BibleReference reference, CancellationToken cancellationToken = default)
    {
        var isRemote = remote.Translations.Any(t => t.Id.Equals(translationId, StringComparison.OrdinalIgnoreCase));
        var book = BookCatalog.Books.FirstOrDefault(b => b.Name == reference.Book);
        if (!isRemote || book is null || !remote.IsConfigured)
        {
            return await inner.GetVersesAsync(translationId, reference, cancellationToken);
        }

        if (cache.IsFresh(translationId, book.Number, reference.Chapter))
        {
            // A reference to a cached chapter renews its window.
            cache.Touch(translationId, book.Number, reference.Chapter);
        }
        else
        {
            // Cold miss on the chapter actually being displayed: fetch it now, but under a
            // short ceiling so a slow network cannot hold up the pipeline.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_options.BlockingTimeoutSeconds));
            try
            {
                await FetchAndSaveAsync(translationId, book.Number, reference.Chapter, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("api.bible fetch for {Reference} ({Translation}) exceeded {Seconds}s",
                    reference, translationId, _options.BlockingTimeoutSeconds);
            }
        }

        // Warm the neighbours and the other translations without blocking this lookup.
        PrefetchAround(book, reference.Chapter);

        return await inner.GetVersesAsync(translationId, reference, cancellationToken);
    }

    /// <summary>
    /// Queues background fetches of chapter ±<see cref="ApiBibleOptions.PrefetchRadius"/>
    /// in every remote translation. Chapters are clamped to the book (a prefetch never
    /// spills into a neighbouring book) and already-fresh or in-flight ones are skipped.
    /// </summary>
    private void PrefetchAround(BookInfo book, int chapter)
    {
        var radius = _options.PrefetchRadius;
        var first = Math.Max(1, chapter - radius);
        var last = Math.Min(book.ChapterCount, chapter + radius);

        // One statement renews the whole window across all translations; the per-chapter
        // work below is then only for what is actually missing.
        cache.TouchRange(remote.Translations.Select(t => t.Id), book.Number, first, last);

        foreach (var translation in remote.Translations)
        {
            var fresh = cache.FreshChapters(translation.Id, book.Number, first, last);
            for (var c = first; c <= last; c++)
            {
                if (fresh.Contains(c))
                {
                    continue;
                }
                var key = (translation.Id, book.Number, c);
                lock (_inFlight)
                {
                    if (!_inFlight.Add(key))
                    {
                        continue;
                    }
                }

                var (translationId, bookNumber, target) = key;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await FetchAndSaveAsync(translationId, bookNumber, target, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Prefetch failed for {Translation} book {Book} ch {Chapter}",
                            translationId, bookNumber, target);
                    }
                    finally
                    {
                        lock (_inFlight)
                        {
                            _inFlight.Remove(key);
                        }
                    }
                });
            }
        }
    }

    private async Task FetchAndSaveAsync(
        string translationId, int bookNumber, int chapter, CancellationToken cancellationToken)
    {
        var fetched = await remote.GetChapterAsync(translationId, bookNumber, chapter, cancellationToken);
        if (fetched is not null)
        {
            cache.Save(fetched);
            logger.LogDebug("Cached {Translation} book {Book} ch {Chapter} ({Count} spans)",
                translationId, bookNumber, chapter, fetched.Verses.Count);
        }
    }
}
