namespace LumosPresenter.Core.Domain;

/// <summary>Content viewport within the 1920x1080 frame, in percent (0..100).</summary>
public sealed record ViewportRect(double X, double Y, double Width, double Height);

/// <summary>
/// Background of a text window. Type is 'solid' | 'image' | 'motion'. Color is used when Type is 'solid';
/// AssetId references a <see cref="MediaAsset"/> when Type is 'image' or 'motion' (null otherwise).
/// </summary>
public sealed record BackgroundConfig(string Type, string Color, string? AssetId = null);

/// <summary>Inner padding of a text viewport, in px at 1920-frame scale.</summary>
public sealed record PaddingConfig(int Top, int Right, int Bottom, int Left);

/// <summary>
/// Styling for the scripture reference line. Position is always relative to the verse text —
/// '{above|below}-{left|center|right}': the first part flows the line above or below the verse
/// block, the second aligns it horizontally within the text column.
/// </summary>
public sealed record ReferenceConfig(
    bool Show,
    string Position, // '{above|below}-{left|center|right}'
    string FontSlug,
    int FontWeight,
    int FontSizePx,
    string Color);

/// <summary>
/// Shared text-rendering block: how verse/lyric text is drawn in its own viewport.
/// Embedded by the scripture and songs configs so each content type sizes and styles independently.
/// </summary>
public sealed record TextDisplayConfig(
    string FontSlug,
    int FontWeight,
    int FontSizePx,      // maximum: the display auto-fits text down from here to fill the viewport
    string TextColor,
    string HorizontalAlign, // 'left' | 'center' | 'right'
    string VerticalAlign,   // 'top' | 'middle' | 'bottom'
    BackgroundConfig Background,
    ViewportRect Viewport,
    PaddingConfig Padding);

/// <summary>Scripture target settings: verse text plus the reference line (scripture-only concept).</summary>
public sealed record ScriptureDisplayConfig(TextDisplayConfig Text, ReferenceConfig Reference);

/// <summary>Song lyrics target settings: text only, no reference line.</summary>
public sealed record SongsDisplayConfig(TextDisplayConfig Text);

/// <summary>
/// Media (image/video) target settings. Fit is 'cover' | 'contain';
/// BackgroundColor fills the window when idle or letterboxing with 'contain'.
///
/// Audio decides whether video sound comes out of THIS display. The level itself is a console
/// control that every display obeys (see MediaTransport), but only the screen actually wired to
/// the speakers should emit it — with two displays both unmuted the room hears the clip twice,
/// slightly apart. It defaults on so a single-display setup, which is most of them, just works;
/// the operator turns it off on the confidence monitor or the second output.
/// </summary>
public sealed record MediaDisplayConfig(
    string Fit,
    string BackgroundColor,
    ViewportRect Viewport,
    bool Audio = true);

/// <summary>
/// Per-display settings container: one independent config per content type (scripture, songs,
/// media), each with its own viewport. Serialized as JSON into displays.config_json and sent
/// verbatim over the API and the 'displayconfig' SSE event.
/// </summary>
public sealed record DisplayConfig(
    ScriptureDisplayConfig Scripture,
    SongsDisplayConfig Songs,
    MediaDisplayConfig Media)
{
    /// <summary>Type-tuned starting points; also the "Reset to Default" target.</summary>
    public static DisplayConfig Default { get; } = new(
        new ScriptureDisplayConfig(
            new TextDisplayConfig("hanken-grotesk", 700, 64, "#ffffff", "center", "middle",
                new BackgroundConfig("solid", "#020617"),
                new ViewportRect(0, 0, 100, 100),
                new PaddingConfig(24, 24, 24, 24)),
            new ReferenceConfig(true, "below-center", "jetbrains-mono", 500, 24, "#adc6ff")),
        new SongsDisplayConfig(
            new TextDisplayConfig("hanken-grotesk", 700, 72, "#ffffff", "center", "middle",
                new BackgroundConfig("solid", "#020617"),
                new ViewportRect(0, 0, 100, 100),
                new PaddingConfig(48, 48, 48, 48))),
        new MediaDisplayConfig("cover", "#000000", new ViewportRect(0, 0, 100, 100)));

    /// <summary>
    /// Blobs from before the per-type container (or malformed ones) deserialize with null
    /// sub-configs and reset wholesale to <see cref="Default"/> (JSON-blob evolution). A
    /// reference position from the old corner-pinned scheme (e.g. 'top-right', 'below-text')
    /// is no longer valid and resets to the default position.
    /// </summary>
    public DisplayConfig Normalized()
    {
        if (Scripture is null || Songs is null || Media is null)
        {
            return Default;
        }
        return IsValidReferencePosition(Scripture.Reference.Position)
            ? this
            : this with
            {
                Scripture = Scripture with
                {
                    Reference = Scripture.Reference with { Position = Default.Scripture.Reference.Position },
                },
            };
    }

    /// <summary>The reference positions the current scheme accepts — all verse-relative.</summary>
    public static bool IsValidReferencePosition(string position) => position is
        "above-left" or "above-center" or "above-right" or
        "below-left" or "below-center" or "below-right";
}

/// <summary>
/// A configured projection display. <see cref="Config"/> is always the effective config:
/// when <see cref="FollowsDisplayId"/> is set, it is the source display's config.
/// </summary>
public sealed record StageDisplay(int Id, string Name, int SortOrder, DisplayConfig Config, int? FollowsDisplayId);

/// <summary>A registered font. 'bundled' fonts ship with the frontend build; 'file' is a future user-installed kind.</summary>
public sealed record FontInfo(
    string Slug,
    string Name,
    string CssFamily,
    string Source,
    string? CssUrl,
    int[] Weights,
    bool Enabled,
    int SortOrder);

/// <summary>
/// A registered background media asset. Metadata lives in the media_assets table; the file itself
/// lives on disk keyed by <see cref="Id"/>. Kind is 'image' | 'motion'. Mirrors <see cref="FontInfo"/>.
/// </summary>
public sealed record MediaAsset(
    string Id,
    string Kind,
    string Title,
    string FileExt,
    string ContentType,
    string Source,
    int SortOrder);
