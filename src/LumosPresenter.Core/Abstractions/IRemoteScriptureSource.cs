using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Abstractions;

/// <summary>
/// A remote provider of chapter text (api.bible today). Kept in Core so the caching
/// layer depends on the abstraction, not the vendor.
/// </summary>
public interface IRemoteScriptureSource
{
    /// <summary>Translation codes this source can serve (e.g. NIV, AMP, MSG).</summary>
    IReadOnlyList<Translation> Translations { get; }

    /// <summary>True when the source is usable (an API key is configured).</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Fetches one whole chapter. Returns null when the chapter is unavailable or the
    /// request fails — callers degrade to whatever is already cached.
    /// </summary>
    Task<RemoteChapter?> GetChapterAsync(
        string translationId, int bookNumber, int chapter, CancellationToken cancellationToken = default);
}

/// <summary>One fetched chapter: its verses (or fused spans) plus the provider's copyright line.</summary>
public sealed record RemoteChapter(
    string TranslationId,
    int BookNumber,
    int Chapter,
    IReadOnlyList<RemoteVerse> Verses,
    string? Copyright);

/// <summary>
/// One unit of text. <paramref name="SpanEnd"/> equals <paramref name="Verse"/> for an
/// ordinary verse, and is the last verse of the block for a paraphrase that fuses several.
/// </summary>
public sealed record RemoteVerse(int Verse, int SpanEnd, string Text);
