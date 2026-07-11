using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Abstractions;

/// <summary>
/// Read-only verse lookup over the local SQLite store. The Dapper implementation
/// lives in the Data project.
/// </summary>
public interface IVerseRepository
{
    Task<IReadOnlyList<Translation>> GetTranslationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a reference to its verses, or an empty list if it does not exist.</summary>
    Task<IReadOnlyList<Verse>> GetVersesAsync(
        string translationId,
        BibleReference reference,
        CancellationToken cancellationToken = default);
}
