using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Abstractions;

/// <summary>
/// The media gallery: images and videos linked from their location on disk. Adding is
/// idempotent per path, so re-adding a file (or re-dropping a folder) never duplicates a tile.
/// </summary>
public interface IMediaLibraryRepository
{
    /// <summary>All gallery items, newest first. Each carries a freshly-resolved Exists flag.</summary>
    Task<IReadOnlyList<MediaLibraryItem>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>A single item by id, or null if it is not in the gallery.</summary>
    Task<MediaLibraryItem?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links a file into the gallery. If the path is already present its existing row is
    /// returned unchanged, so the caller can add a batch without pre-filtering duplicates.
    /// </summary>
    Task<MediaLibraryItem> AddAsync(
        string sourcePath, string kind, string title, string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>Unlinks an item. The file on disk is never touched. False if it did not exist.</summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
