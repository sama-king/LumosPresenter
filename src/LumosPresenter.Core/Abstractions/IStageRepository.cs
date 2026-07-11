using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Abstractions;

/// <summary>
/// Stage configuration storage: projection displays and the font registry.
/// Returned displays always carry their effective config (a follower resolves
/// to its source display's config). The Dapper implementation lives in Data.
/// </summary>
public interface IStageRepository
{
    Task<IReadOnlyList<StageDisplay>> GetDisplaysAsync(CancellationToken cancellationToken = default);

    Task<StageDisplay?> GetDisplayAsync(int id, CancellationToken cancellationToken = default);

    Task<StageDisplay> CreateDisplayAsync(
        string name, DisplayConfig config, int? followsDisplayId = null, CancellationToken cancellationToken = default);

    /// <summary>Replaces a display's own config. Returns null if the display does not exist.</summary>
    Task<StageDisplay?> UpdateConfigAsync(int id, DisplayConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets or clears the follow link. Detaching snapshots the source's config into the
    /// display's own config so it keeps its look. Returns null if the display does not exist.
    /// </summary>
    Task<StageDisplay?> SetFollowsAsync(int id, int? followsDisplayId, CancellationToken cancellationToken = default);

    /// <summary>Ids of displays that follow the given display.</summary>
    Task<IReadOnlyList<int>> GetFollowerIdsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Deletes a display; its followers are detached with its config snapshotted first.</summary>
    Task<bool> DeleteDisplayAsync(int id, CancellationToken cancellationToken = default);

    Task<int> CountDisplaysAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FontInfo>> GetFontsAsync(bool enabledOnly = true, CancellationToken cancellationToken = default);

    /// <summary>Background media assets (uploaded images and looping videos), newest first.</summary>
    Task<IReadOnlyList<MediaAsset>> GetMediaAssetsAsync(CancellationToken cancellationToken = default);

    /// <summary>A single media asset by id, or null if it does not exist.</summary>
    Task<MediaAsset?> GetMediaAssetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Registers an uploaded media asset (the file is already written to disk).</summary>
    Task<MediaAsset> AddMediaAssetAsync(MediaAsset asset, CancellationToken cancellationToken = default);

    /// <summary>Removes a media asset from the registry. Returns false if it did not exist.</summary>
    Task<bool> DeleteMediaAssetAsync(string id, CancellationToken cancellationToken = default);
}
