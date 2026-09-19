using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Abstractions;

/// <summary>
/// Song library storage. Sections have no identity outside their song and are
/// replaced wholesale on save. The Dapper implementation lives in the Data project.
/// </summary>
public interface ISongRepository
{
    /// <summary>
    /// Case-insensitive substring search over titles and lyrics, title matches first; null/blank
    /// returns the whole library, by title.
    /// </summary>
    Task<IReadOnlyList<SongSummary>> SearchAsync(string? query, CancellationToken cancellationToken = default);

    Task<Song?> GetAsync(int id, CancellationToken cancellationToken = default);

    Task<Song> CreateAsync(SongDraft draft, CancellationToken cancellationToken = default);

    /// <summary>Replaces the song's fields and sections. Returns null if the song does not exist.</summary>
    Task<Song?> UpdateAsync(int id, SongDraft draft, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
