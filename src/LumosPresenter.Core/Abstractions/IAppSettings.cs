namespace LumosPresenter.Core.Abstractions;

/// <summary>
/// Installation settings the operator owns — stored with their data rather than in
/// configuration, so they can be changed from the UI and survive an app update.
/// Reads are synchronous and cheap: implementations keep the small set in memory.
/// </summary>
public interface IAppSettings
{
    /// <summary>The stored value, or null when the setting has never been set.</summary>
    string? Get(string key);

    /// <summary>
    /// Stores a value, replacing any previous one. A null or blank value clears the
    /// setting, so "save an empty key" and "remove the key" are the same operation.
    /// </summary>
    void Set(string key, string? value);
}

/// <summary>Names of the settings the app stores. Kept together so they cannot drift.</summary>
public static class AppSettingKeys
{
    /// <summary>The user's api.bible key. Absent means online sources are unavailable.</summary>
    public const string ApiBibleKey = "apiBible.key";

    /// <summary>
    /// Set once the configured (.env / environment) api.bible key has been carried into the
    /// database. Absence of the key itself cannot serve as that marker: an operator who
    /// removes their key would otherwise have a stale .env re-adopted at every startup.
    /// </summary>
    public const string ApiBibleKeyAdopted = "apiBible.key.adopted";

    /// <summary>
    /// Comma-separated ids of every bundled background ever registered. It is what lets a
    /// default the operator deleted stay deleted, instead of returning at the next startup.
    /// </summary>
    public const string BundledBackgroundsSeeded = "media.bundledBackgrounds.seeded";
}
