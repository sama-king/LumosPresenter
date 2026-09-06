namespace LumosPresenter.Data.Remote;

/// <summary>Bound from the "ApiBible" configuration section.</summary>
public sealed class ApiBibleOptions
{
    public const string SectionName = "ApiBible";

    /// <summary>
    /// Legacy seed for the api.bible key. The key now lives in the database (set from the
    /// Settings page) so each user supplies their own; this is read ONCE, on a database that
    /// has no key yet, so an existing .env or environment variable keeps working after the
    /// upgrade. Setting or clearing the key in the UI is authoritative from then on.
    /// </summary>
    public string? Key { get; set; }

    public string BaseUrl { get; set; } = "https://api.scripture.api.bible/v1/";

    /// <summary>How long a fetched chapter stays valid; refreshed on every reference (sliding).</summary>
    public int CacheDays { get; set; } = 14;

    /// <summary>How many chapters either side of a resolved one to prefetch.</summary>
    public int PrefetchRadius { get; set; } = 1;

    /// <summary>
    /// Ceiling on a blocking (cold-miss) fetch, so a slow network cannot stall the
    /// transcription pipeline. Background prefetches use the longer HttpClient timeout.
    /// Set above the cold-start cost (first connection pays DNS + TLS, measured at just
    /// over 5s; warm requests are ~1s) so the very first lookup is not cut short.
    /// </summary>
    public int BlockingTimeoutSeconds { get; set; } = 12;

    /// <summary>Remote translations to expose, as code → api.bible bible id.</summary>
    public Dictionary<string, RemoteTranslationOptions> Translations { get; set; } = new()
    {
        ["NIV"] = new("78a9f6124f344018-01", "New International Version", "en"),
        ["AMP"] = new("a81b73293d3080c9-01", "Amplified Bible", "en"),
        ["MSG"] = new("6f11a7de016f942e-01", "The Message", "en"),
    };
}

/// <summary>One remote translation's identity on the provider.</summary>
public sealed record RemoteTranslationOptions(string BibleId, string Name, string Language)
{
    public RemoteTranslationOptions() : this("", "", "en") { }
}
