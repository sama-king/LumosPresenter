namespace LumosPresenter.Core.Domain;

/// <summary>A Bible translation available in the local store (bundled, imported, or remote).</summary>
/// <param name="Source">
/// Where the text comes from: "bundled" / "easyworship" are held locally and work offline;
/// "api.bible" is fetched on demand and cached, so it needs the internet for chapters that
/// have not been cached yet. The console surfaces this so an operator knows what is safe
/// to rely on with no connection.
/// </param>
public sealed record Translation(string Id, string Name, string Language, string Source = "bundled");
