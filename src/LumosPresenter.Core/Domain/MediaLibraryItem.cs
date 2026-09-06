namespace LumosPresenter.Core.Domain;

/// <summary>
/// One gallery item in the media library: an image or video the operator projects as content.
/// The file is <em>linked</em>, not copied — <see cref="SourcePath"/> is an absolute path on the
/// machine running the WebHost, and is the only path the file endpoint will ever read from.
/// <see cref="Exists"/> is resolved per read (not stored), so a file moved or deleted behind the
/// app's back surfaces in the gallery as an error thumbnail instead of a broken fetch.
/// Distinct from <see cref="MediaAsset"/>, which registers *background* files copied into media/.
/// </summary>
public sealed record MediaLibraryItem(
    string Id,
    string SourcePath,
    string Kind,
    string Title,
    string ContentType,
    int SortOrder,
    bool Exists);
