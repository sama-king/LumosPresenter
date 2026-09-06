using System.Net.Http.Json;
using System.Text.Json;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using LumosPresenter.Core.Parsing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Data.Remote;

/// <summary>
/// Fetches chapters from api.bible (scripture.api.bible). Chapters are requested as
/// structured JSON rather than HTML so verse boundaries survive; the walk below is the
/// only place that understands the provider's document shape.
/// </summary>
public sealed class ApiBibleScriptureSource : IRemoteScriptureSource
{
    private readonly HttpClient _http;
    private readonly ApiBibleOptions _options;
    private readonly IAppSettings _settings;
    private readonly ILogger<ApiBibleScriptureSource> _logger;

    public ApiBibleScriptureSource(
        HttpClient http,
        IOptions<ApiBibleOptions> options,
        IAppSettings settings,
        ILogger<ApiBibleScriptureSource> logger)
    {
        _options = options.Value;
        _settings = settings;
        _logger = logger;
        _http = http;
        _http.BaseAddress = new Uri(_options.BaseUrl);
        Translations =
        [
            .. _options.Translations.Select(kv =>
                new Translation(kv.Key, kv.Value.Name, kv.Value.Language, "api.bible")),
        ];
    }

    public IReadOnlyList<Translation> Translations { get; }

    /// <summary>
    /// The operator's key, read fresh from settings on every use. It is deliberately NOT
    /// a default header on the HttpClient: the key can be entered, changed, or removed
    /// from the UI at any time, and this source is a long-lived typed client.
    /// </summary>
    private string? Key => _settings.Get(AppSettingKeys.ApiBibleKey);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Key);

    public async Task<RemoteChapter?> GetChapterAsync(
        string translationId, int bookNumber, int chapter, CancellationToken cancellationToken = default)
    {
        if (Key is not { Length: > 0 } key
            || !_options.Translations.TryGetValue(translationId, out var translation)
            || UsfmBookCodes.ForBookNumber(bookNumber) is not { } usfm)
        {
            return null;
        }

        // Verse numbers, notes, titles and chapter numbers are all excluded: we want the
        // scripture text alone, and the verse markers would otherwise appear inline as
        // stray digits ("1Now there was a Pharisee...").
        var url = $"bibles/{translation.BibleId}/chapters/{usfm}.{chapter}"
            + "?content-type=json&include-verse-numbers=false"
            + "&include-notes=false&include-titles=false&include-chapter-numbers=false";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("api-key", key);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 404 simply means the chapter does not exist in that translation.
                var level = response.StatusCode == System.Net.HttpStatusCode.NotFound
                    ? LogLevel.Debug
                    : LogLevel.Warning;
                _logger.Log(level, "api.bible {Status} for {Translation} {Usfm}.{Chapter}",
                    (int)response.StatusCode, translationId, usfm, chapter);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
            if (!payload.TryGetProperty("data", out var data))
            {
                return null;
            }

            var verses = ExtractVerses(data);
            if (verses.Count == 0)
            {
                return null;
            }

            var copyright = data.TryGetProperty("copyright", out var c) ? c.GetString() : null;
            return new RemoteChapter(translationId, bookNumber, chapter, verses, copyright?.Trim());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "api.bible fetch failed for {Translation} {Usfm}.{Chapter}",
                translationId, usfm, chapter);
            return null;
        }
    }

    /// <summary>
    /// Walks the provider's content tree and accumulates text per verse span. Only text
    /// nodes carrying an attrs.verseId are scripture; the "verse" tags themselves are
    /// printed markers. A node's attrs.verseOrgIds lists every verse the text covers —
    /// more than one means a paraphrase fused them (MSG), which becomes a single span.
    /// </summary>
    internal static List<RemoteVerse> ExtractVerses(JsonElement data)
    {
        // Keyed by first verse of the span, preserving document order.
        var spans = new Dictionary<int, (int SpanEnd, List<string> Parts)>();
        var order = new List<int>();

        void Walk(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var child in node.EnumerateArray())
                {
                    Walk(child);
                }
                return;
            }
            if (node.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (node.TryGetProperty("type", out var type)
                && type.GetString() == "text"
                && node.TryGetProperty("attrs", out var attrs)
                && attrs.TryGetProperty("verseId", out _)
                && node.TryGetProperty("text", out var textNode)
                && textNode.GetString() is { Length: > 0 } text)
            {
                var (start, end) = SpanOf(attrs);
                if (start > 0)
                {
                    if (!spans.TryGetValue(start, out var existing))
                    {
                        existing = (end, []);
                        order.Add(start);
                    }
                    existing.Parts.Add(text);
                    spans[start] = (Math.Max(existing.SpanEnd, end), existing.Parts);
                }
            }

            if (node.TryGetProperty("items", out var items))
            {
                Walk(items);
            }
        }

        if (data.TryGetProperty("content", out var content))
        {
            Walk(content);
        }

        var result = new List<RemoteVerse>(order.Count);
        foreach (var start in order)
        {
            var (end, parts) = spans[start];
            var text = string.Join(string.Empty, parts).Replace('\n', ' ').Trim();
            // Collapse the runs of whitespace that joining adjacent nodes can produce.
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s{2,}", " ");
            if (text.Length > 0)
            {
                result.Add(new RemoteVerse(start, end, text));
            }
        }
        return result;
    }

    /// <summary>
    /// Reads the verse span a text node covers from its attrs — preferring verseOrgIds
    /// ("ROM.8.5".."ROM.8.8" → 5..8) and falling back to the single verseId.
    /// </summary>
    private static (int Start, int End) SpanOf(JsonElement attrs)
    {
        var numbers = new List<int>();
        if (attrs.TryGetProperty("verseOrgIds", out var orgIds) && orgIds.ValueKind == JsonValueKind.Array)
        {
            foreach (var id in orgIds.EnumerateArray())
            {
                if (TrailingNumber(id.GetString()) is { } n)
                {
                    numbers.Add(n);
                }
            }
        }
        if (numbers.Count == 0
            && attrs.TryGetProperty("verseId", out var verseId)
            && TrailingNumber(verseId.GetString()) is { } single)
        {
            numbers.Add(single);
        }
        return numbers.Count == 0 ? (0, 0) : (numbers.Min(), numbers.Max());
    }

    // "ROM.8.5" → 5. Ranged ids ("ROM.8.1-ROM.8.3") are handled via verseOrgIds instead.
    private static int? TrailingNumber(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }
        var lastDot = id.LastIndexOf('.');
        return lastDot >= 0 && int.TryParse(id[(lastDot + 1)..], out var n) ? n : null;
    }
}
