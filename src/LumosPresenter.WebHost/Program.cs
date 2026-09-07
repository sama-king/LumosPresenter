using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using LumosPresenter.Audio;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using LumosPresenter.Core.Parsing;
using LumosPresenter.Data;
using LumosPresenter.Speech;
using LumosPresenter.WebHost.Api;
using LumosPresenter.WebHost.Realtime;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;

// The api.bible key now lives in the database, set from the console, so each user supplies
// their own. A .env at the repository root is still read here purely as a development
// convenience: on a database with no key yet it is adopted once (see DatabaseInitializer),
// after which the stored key is authoritative. Both the binary's location and the working
// directory are searched: neither alone survives every way the app is started (published
// .app, launcher-spawned child, dotnet run).
LumosPresenter.WebHost.DotEnv.Load(AppContext.BaseDirectory, Directory.GetCurrentDirectory());

var builder = WebApplication.CreateBuilder(args);

// Media uploads are audio-only, but full-length sermon audio still runs large.
const long MaxUploadBytes = 200L * 1024 * 1024; // 200 MB
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = MaxUploadBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = MaxUploadBytes;
});

// The file sink's path in configuration is relative to the content root, like the model
// and database paths below — the app is started from several different working
// directories (published .app, launcher-spawned child, dotnet run) and the logs have to
// land in one predictable place for the launcher to show them.
builder.Host.UseSerilog((context, loggerConfiguration) =>
{
    // Found by sink name rather than by array index, so reordering or adding a sink in
    // appsettings does not silently send the logs back to the working directory.
    foreach (var sink in context.Configuration.GetSection("Serilog:WriteTo").GetChildren())
    {
        if (sink["Name"] != "File" || sink["Args:path"] is not { Length: > 0 } configured)
        {
            continue;
        }
        sink["Args:path"] = Path.GetFullPath(configured, context.HostingEnvironment.ContentRootPath);
    }
    loggerConfiguration.ReadFrom.Configuration(context.Configuration);
});

builder.Services.AddLumosData(builder.Configuration);
builder.Services.AddLumosAudio();
builder.Services.AddLumosSpeech(builder.Configuration);

// Model and database paths in configuration are relative to the content root, not the process CWD.
builder.Services.PostConfigure<SpeechOptions>(options =>
{
    var root = builder.Environment.ContentRootPath;
    options.Whisper.ModelPath = Path.GetFullPath(options.Whisper.ModelPath, root);
    options.SherpaOnnx.EncoderPath = Path.GetFullPath(options.SherpaOnnx.EncoderPath, root);
    options.SherpaOnnx.DecoderPath = Path.GetFullPath(options.SherpaOnnx.DecoderPath, root);
    options.SherpaOnnx.JoinerPath = Path.GetFullPath(options.SherpaOnnx.JoinerPath, root);
    options.SherpaOnnx.TokensPath = Path.GetFullPath(options.SherpaOnnx.TokensPath, root);
});
builder.Services.PostConfigure<DataOptions>(options =>
{
    var root = builder.Environment.ContentRootPath;
    options.DatabasePath = Path.GetFullPath(options.DatabasePath, root);
    options.SeedDirectory = Path.GetFullPath(options.SeedDirectory, root);
});

builder.Services.AddSingleton<EventBroadcaster>();
builder.Services.AddSingleton<SessionRecorder>();
builder.Services.AddSingleton<MediaDecoder>();
builder.Services.AddSingleton<TranslationState>();
builder.Services.AddSingleton<LiveState>();
builder.Services.AddSingleton<TranscriptionPipeline>();
builder.Services.AddHostedService<AudioLevelPublisher>();

var app = builder.Build();

// Migrations + first-run seeding of bundled translations (KJV, ASV, BSB).
app.Services.GetRequiredService<LumosPresenter.Data.Seeding.DatabaseInitializer>().Initialize();
app.Services.GetRequiredService<TranslationState>()
    .SetDefault(builder.Configuration.GetValue<string>("Data:DefaultTranslation") ?? "KJV");

// Warm the api.bible connection so the first verse lookup does not pay the TLS handshake.
if (app.Services.GetRequiredService<IVerseRepository>() is LumosPresenter.Data.Remote.CachingVerseRepository caching)
{
    caching.WarmUp();
}

// Apply the configured parser window at startup (default 15 if unset).
var configuredWindow = builder.Configuration.GetValue<int?>("Parser:UtteranceWindow");
if (configuredWindow is { } window)
{
    app.Services.GetRequiredService<TranscriptionPipeline>().UtteranceWindow = window;
}

// Auto-live confidence gate (default 0.75 if unset) — server-side so detections
// reach the displays regardless of which console page is open.
var configuredConfidence = builder.Configuration.GetValue<double?>("Parser:AutoLiveConfidence");
if (configuredConfidence is { } confidence)
{
    app.Services.GetRequiredService<TranscriptionPipeline>().AutoLiveConfidence = confidence;
}

// The operator console polls /api/audio/level 10×/second for the level meter the whole
// time it is open, and the launcher polls /healthz to see whether the server is up. Logged
// at Information those two drown everything else — a single open console writes ~860k lines
// a day, and the file sink flushes every second to do it. Demote the pollers to Verbose so
// they fall below the configured minimum, while still logging any that actually fail.
app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, _, exception) =>
    {
        if (exception is not null || httpContext.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }
        var path = httpContext.Request.Path;
        var isPoll = path.StartsWithSegments("/healthz")
            || path.StartsWithSegments("/api/audio/level");
        return isPoll ? LogEventLevel.Verbose : LogEventLevel.Information;
    };
});

// The React build (frontend/) is emitted into wwwroot; / serves the SPA,
// which routes /admin (operator console) and /display (projection surface).
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

var api = app.MapGroup("/api");

api.MapGet("/status", (
    TranscriptionPipeline pipeline,
    ISpeechEngineProvider engines,
    IAudioCapture capture,
    SessionRecorder recorder,
    TranslationState translation,
    IOptions<SpeechOptions> speech) =>
    Results.Ok(new
    {
        listening = pipeline.IsListening,
        engine = engines.Current.Name,
        // Null when the active engine can run; otherwise why it can't, so the console can
        // say so instead of leaving "Listening: false" unexplained.
        engineError = engines.Current.ReadinessError,
        engines = engines.AvailableEngines,
        deviceId = capture.DeviceId,
        recording = recorder.Enabled,
        lastRecording = recorder.LastRecordingPath is { } p ? Path.GetFileName(p) : null,
        utteranceWindow = pipeline.UtteranceWindow,
        autoLiveConfidence = pipeline.AutoLiveConfidence,
        translation = translation.Current,
        vadThreshold = speech.Value.Vad.EnergyThreshold,
        // The console can be open on a different machine than the server (a display on the
        // LAN), so which OS this is has to come from the server, not the browser. The
        // acceleration indicator is a Windows concern: macOS gets CoreML automatically and
        // has no Vulkan path.
        platform = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos"
            : "linux",
        // Null until a model has been loaded — the native library resolves lazily.
        accelerator = WhisperSpeechEngine.LoadedRuntime,
    }));

// --- Bible translations: list available, switch the active one ---

api.MapGet("/translations", async (IVerseRepository repository, TranslationState translation, CancellationToken ct) =>
    Results.Ok(new
    {
        current = translation.Current,
        translations = await repository.GetTranslationsAsync(ct),
    }));

api.MapPost("/translation/{code}", async (
    string code, TranslationState translation, IVerseRepository repository,
    IRemoteScriptureSource remote, CancellationToken ct) =>
{
    // Remote translations are seeded as rows whether or not a key exists, so without this
    // guard they can be selected — from the operator console, or by voice — and then
    // resolve to nothing. Refusing here covers every caller, not just the console's UI.
    if (!remote.IsConfigured
        && remote.Translations.Any(t => t.Id.Equals(code, StringComparison.OrdinalIgnoreCase)))
    {
        return Results.BadRequest(new
        {
            message = $"{code} is an online translation. Add an api.bible key to use it.",
        });
    }
    try
    {
        await translation.SelectAsync(code, repository, ct);
        return Results.Ok(new { translation = translation.Current });
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

// --- Online sources: the operator's own api.bible key ---
//
// The key is stored per installation, in the database, so each user supplies their own and
// can change it without a rebuild or a restart. It is write-only over the API: a GET says
// whether one is set and shows only its last four characters, so the console can report
// state without ever handing the secret back to a browser.

api.MapGet("/settings/api-bible", (IAppSettings settings) =>
{
    var key = settings.Get(AppSettingKeys.ApiBibleKey);
    return Results.Ok(new
    {
        configured = !string.IsNullOrWhiteSpace(key),
        hint = MaskKey(key),
    });
});

api.MapPost("/settings/api-bible", (
    ApiBibleKeyRequest request, IAppSettings settings, IVerseRepository repository) =>
{
    var key = request.Key?.Trim();
    if (string.IsNullOrEmpty(key))
    {
        return Results.BadRequest(new { message = "Enter an api.bible key." });
    }
    settings.Set(AppSettingKeys.ApiBibleKey, key);
    // A brand-new key means a cold connection; warm it now rather than making the
    // operator's first lookup pay the TLS handshake.
    if (repository is LumosPresenter.Data.Remote.CachingVerseRepository warm)
    {
        warm.WarmUp();
    }
    return Results.Ok(new { configured = true, hint = MaskKey(key) });
});

// Clearing the key is how an operator turns online sources off for good: with none set
// the remote source reports itself unconfigured and every lookup stays local.
api.MapDelete("/settings/api-bible", (IAppSettings settings) =>
{
    settings.Set(AppSettingKeys.ApiBibleKey, null);
    return Results.Ok(new { configured = false, hint = (string?)null });
});

// How many utterances a detected book/chapter stays in context before it decays,
// bridging pauses and filler between "Ephesians", "the fifth chapter", and "verse twelve".
api.MapPost("/parser/window/{value:int}", (int value, TranscriptionPipeline pipeline) =>
{
    pipeline.UtteranceWindow = value;
    return Results.Ok(new { utteranceWindow = pipeline.UtteranceWindow });
});

// How sure the parser must be before a detection goes live without an operator pushing it.
// Sent as a percentage so the URL carries no decimal point; clamped to the same 0.5–1.0
// band the settings slider offers, since a gate below half would fire on near-noise.
api.MapPost("/parser/confidence/{percent:int}", (int percent, TranscriptionPipeline pipeline) =>
{
    pipeline.AutoLiveConfidence = Math.Clamp(percent, 50, 100) / 100.0;
    return Results.Ok(new { autoLiveConfidence = pipeline.AutoLiveConfidence });
});

// How loud a frame must be to count as speech rather than room noise. Sent in thousandths
// so the URL carries no decimal point, the same trick /parser/confidence uses for percent.
// Clamped to 0.001–0.1: at zero every frame is speech and utterances never close, and past
// 0.1 normal speech falls below the gate and nothing is heard at all.
//
// Takes effect on the next utterance: the engines read Vad off the shared SpeechOptions
// each time they chunk, so there is no need to restart the pipeline.
// Deliberately publishes no SSE event, matching /parser/window and /parser/confidence: the
// console that made the change learns the value from the response, and subscribers treat a
// `status` event as carrying the full listening/engine state — a partial one would blank
// the footer's toggle and engine label.
api.MapPost("/speech/vad/threshold/{thousandths:int}", (
    int thousandths, IOptions<SpeechOptions> speech) =>
{
    var value = Math.Clamp(thousandths, 1, 100) / 1000.0;
    speech.Value.Vad = speech.Value.Vad with { EnergyThreshold = value };
    return Results.Ok(new { vadThreshold = value });
});

// --- Scripture lookup: synchronous search + chapter fetch for the operator console ---

// Canonical book names, in canonical order — powers the search box's autocomplete.
api.MapGet("/scripture/books", () =>
    Results.Ok(new { books = BookCatalog.Books.Select(b => b.Name) }));

api.MapGet("/scripture/search", async (
    string? q, IVerseRepository repository, TranslationState translation, CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(q))
    {
        return Results.BadRequest(new { message = "Query 'q' is required." });
    }

    // A fresh parser per request: the pipeline's parser carries live utterance
    // context that a typed query must never read or pollute.
    var references = new ReferenceParser().Parse(q);
    var results = new List<object>(references.Count);
    foreach (var reference in references)
    {
        var verses = await repository.GetVersesAsync(translation.Current, reference, ct);
        results.Add(new
        {
            display = reference.ToString(),
            book = reference.Book,
            chapter = reference.Chapter,
            verseStart = reference.VerseStart,
            verseEnd = reference.VerseEnd,
            confidence = reference.Confidence,
            verses = verses.Select(v => new { number = v.Number, text = v.Text }),
        });
    }
    return Results.Ok(new { query = q, translation = translation.Current, results });
});

api.MapGet("/scripture/chapter/{book}/{chapter:int}", async (
    string book, int chapter, string? translation,
    IVerseRepository repository, TranslationState translationState, CancellationToken ct) =>
{
    // The repository matches book names case-sensitively; canonicalize first.
    var info = BookCatalog.Books.FirstOrDefault(
        b => string.Equals(b.Name, book, StringComparison.OrdinalIgnoreCase));
    if (info is null)
    {
        return Results.NotFound(new { message = $"Unknown book '{book}'." });
    }
    if (chapter < 1 || chapter > info.ChapterCount)
    {
        return Results.NotFound(new { message = $"{info.Name} has {info.ChapterCount} chapters." });
    }

    var code = string.IsNullOrWhiteSpace(translation) ? translationState.Current : translation;
    var verses = await repository.GetVersesAsync(
        code, new BibleReference(info.Name, chapter, null, null, 1.0), ct);
    if (verses.Count == 0)
    {
        return Results.NotFound(new { message = $"No verses for {info.Name} {chapter} in '{code}'." });
    }
    return Results.Ok(new
    {
        translation = code,
        book = info.Name,
        bookNumber = info.Number,
        chapter,
        chapterCount = info.ChapterCount,
        verses = verses.Select(v => new { number = v.Number, text = v.Text }),
    });
});

// --- Song library: CRUD + .txt import. Sections are projection slides; the lyrics
//     parser (Core) is the single splitting truth shared with imports. ---

api.MapSongs();
api.MapMediaLibrary();

// --- Live display channel: what the projection displays show right now ---

api.MapPost("/live", (LiveRequest request, LiveState live) =>
{
    if (request.Kind is not (null or "scripture" or "song" or "media"))
    {
        return Results.BadRequest(new { message = $"Unknown live kind '{request.Kind}'." });
    }
    // A media push is a picture, not a passage: it names a gallery item and carries no text,
    // so only the text kinds are held to the reference/text requirement.
    if (request.Kind == "media")
    {
        if (string.IsNullOrWhiteSpace(request.MediaId))
        {
            return Results.BadRequest(new { message = "A media id is required for a media push." });
        }
    }
    else if (string.IsNullOrWhiteSpace(request.Reference) || string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { message = "Reference and text are required." });
    }
    var item = new LiveItem(
        Guid.NewGuid().ToString("N"),
        request.Reference?.Trim() ?? string.Empty,
        request.Text?.Trim() ?? string.Empty,
        request.Translation,
        string.IsNullOrWhiteSpace(request.Source) ? "manual" : request.Source,
        DateTimeOffset.UtcNow,
        request.Book,
        request.Chapter,
        request.VerseStart,
        request.VerseEnd ?? request.VerseStart,
        request.Kind ?? "scripture",
        request.MediaId,
        request.MediaKind,
        request.MediaLoop ?? true);
    live.Show(item);
    return Results.Ok(item);
});

api.MapGet("/live", (LiveState live) =>
    live.Current is { } item ? Results.Ok(item) : Results.NoContent());

// A display reporting that its non-looping video finished. The console owns queue order, so
// the server only relays: it confirms the report is about what is actually live (a display
// showing a stale item must not advance anything) and republishes it as an SSE event for
// whichever console is driving. Every display showing the item reports, so the console
// de-duplicates by live-item id on its side.
api.MapPost("/live/media/ended", (MediaEndedRequest request, LiveState live, EventBroadcaster broadcaster) =>
{
    var current = live.Current;
    if (current is null || current.Kind != "media" || current.Id != request.Id)
    {
        return Results.Ok(new { accepted = false });
    }
    broadcaster.Publish(new PipelineEvent("mediaended", new { id = request.Id }));
    return Results.Ok(new { accepted = true });
});

api.MapPost("/live/clear", (LiveState live) =>
{
    live.Clear();
    return Results.Ok();
});

// --- Stage configuration: per-display styling of the single live feed. A display with a
//     follow link permanently mirrors its source display's settings (one level, no chains);
//     config changes fan out over SSE to the display and all of its followers. ---

api.MapGet("/fonts", async (IStageRepository stage, CancellationToken ct) =>
    Results.Ok(new { fonts = await stage.GetFontsAsync(enabledOnly: true, ct) }));

api.MapGet("/displays", async (IStageRepository stage, CancellationToken ct) =>
    Results.Ok(new
    {
        defaultConfig = DisplayConfig.Default,
        displays = await stage.GetDisplaysAsync(ct),
    }));

api.MapGet("/displays/{id:int}", async (int id, IStageRepository stage, CancellationToken ct) =>
    await stage.GetDisplayAsync(id, ct) is { } display
        ? Results.Ok(display)
        : Results.NotFound(new { message = $"Display {id} not found." }));

api.MapPost("/displays", async (CreateDisplayRequest request, IStageRepository stage, CancellationToken ct) =>
{
    var config = DisplayConfig.Default;
    if (request.UseSettingsOfDisplayId is { } sourceId)
    {
        var source = await stage.GetDisplayAsync(sourceId, ct);
        if (source is null)
        {
            return Results.NotFound(new { message = $"Display {sourceId} not found." });
        }
        if (source.FollowsDisplayId is not null)
        {
            return Results.BadRequest(new { message = $"{source.Name} already uses another display's settings — link to that display instead." });
        }
        config = source.Config;
    }
    var name = string.IsNullOrWhiteSpace(request.Name)
        ? $"Display {await stage.CountDisplaysAsync(ct) + 1}"
        : request.Name.Trim();
    return Results.Ok(await stage.CreateDisplayAsync(name, config, request.UseSettingsOfDisplayId, ct));
});

api.MapPut("/displays/{id:int}/config", async (
    int id, DisplayConfig config, IStageRepository stage, EventBroadcaster broadcaster, CancellationToken ct) =>
{
    var existing = await stage.GetDisplayAsync(id, ct);
    if (existing is null)
    {
        return Results.NotFound(new { message = $"Display {id} not found." });
    }
    if (existing.FollowsDisplayId is { } sourceId)
    {
        return Results.BadRequest(new { message = $"This display uses the settings of display {sourceId} — detach it first." });
    }
    config = config.Normalized(); // null sub-configs (old/partial payloads) reset to defaults
    if (await ValidateConfigAsync(config, stage, ct) is { } error)
    {
        return Results.BadRequest(new { message = error });
    }
    var display = (await stage.UpdateConfigAsync(id, ClampConfig(config), ct))!;
    await PublishDisplayConfigAsync(display, stage, broadcaster, ct);
    return Results.Ok(display);
});

api.MapPut("/displays/{id:int}/source", async (
    int id, SetDisplaySourceRequest request, IStageRepository stage, EventBroadcaster broadcaster, CancellationToken ct) =>
{
    var existing = await stage.GetDisplayAsync(id, ct);
    if (existing is null)
    {
        return Results.NotFound(new { message = $"Display {id} not found." });
    }
    if (request.FollowsDisplayId is { } targetId)
    {
        if (targetId == id)
        {
            return Results.BadRequest(new { message = "A display cannot use its own settings." });
        }
        var target = await stage.GetDisplayAsync(targetId, ct);
        if (target is null)
        {
            return Results.NotFound(new { message = $"Display {targetId} not found." });
        }
        if (target.FollowsDisplayId is not null)
        {
            return Results.BadRequest(new { message = $"{target.Name} already uses another display's settings — link to that display instead." });
        }
        if ((await stage.GetFollowerIdsAsync(id, ct)).Count > 0)
        {
            return Results.BadRequest(new { message = "Other displays use this display's settings — detach them first." });
        }
    }
    var display = (await stage.SetFollowsAsync(id, request.FollowsDisplayId, ct))!;
    await PublishDisplayConfigAsync(display, stage, broadcaster, ct);
    return Results.Ok(display);
});

api.MapPost("/displays/{id:int}/reset", async (
    int id, IStageRepository stage, EventBroadcaster broadcaster, CancellationToken ct) =>
{
    var existing = await stage.GetDisplayAsync(id, ct);
    if (existing is null)
    {
        return Results.NotFound(new { message = $"Display {id} not found." });
    }
    if (existing.FollowsDisplayId is { } sourceId)
    {
        return Results.BadRequest(new { message = $"This display uses the settings of display {sourceId} — detach it first." });
    }
    var display = (await stage.UpdateConfigAsync(id, DisplayConfig.Default, ct))!;
    await PublishDisplayConfigAsync(display, stage, broadcaster, ct);
    return Results.Ok(display);
});

api.MapDelete("/displays/{id:int}", async (int id, IStageRepository stage, CancellationToken ct) =>
{
    if (await stage.GetDisplayAsync(id, ct) is null)
    {
        return Results.NotFound(new { message = $"Display {id} not found." });
    }
    if (await stage.CountDisplaysAsync(ct) <= 1)
    {
        return Results.BadRequest(new { message = "At least one display is required." });
    }
    // Followers are detached with this display's config snapshotted, so their
    // effective config — and what their open windows show — does not change.
    await stage.DeleteDisplayAsync(id, ct);
    return Results.Ok();
});

// LAN address for the display URLs: projectors/OBS on other devices can't use
// localhost, and the server binds 0.0.0.0 so any interface address works.
api.MapGet("/network", (IServer server) =>
{
    var port = 5170;
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
    if (addresses?.Select(a => Uri.TryCreate(a, UriKind.Absolute, out var u) ? u.Port : (int?)null)
            .FirstOrDefault(p => p is not null) is { } boundPort)
    {
        port = boundPort;
    }

    var ips = NetworkInterface.GetAllNetworkInterfaces()
        .Where(nic => nic.OperationalStatus == OperationalStatus.Up
            && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback
            && nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
        .SelectMany(nic => nic.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
        .Select(ip => ip.ToString())
        .Where(ip => !ip.StartsWith("169.254.", StringComparison.Ordinal)) // link-local noise
        .Distinct()
        // Prefer the ranges a church LAN actually hands out over VPN/virtual leftovers.
        .OrderByDescending(ip => ip.StartsWith("192.168.", StringComparison.Ordinal))
        .ThenByDescending(ip => ip.StartsWith("10.", StringComparison.Ordinal))
        .ToArray();

    return Results.Ok(new { host = ips.FirstOrDefault(), ips, port });
});

// The affected display and every follower restyle live; each follower gets the
// event under its own id so open display windows can filter without link-awareness.
static async Task PublishDisplayConfigAsync(
    StageDisplay display, IStageRepository stage, EventBroadcaster broadcaster, CancellationToken ct)
{
    broadcaster.Publish(new PipelineEvent("displayconfig", new { displayId = display.Id, config = display.Config }));
    foreach (var followerId in await stage.GetFollowerIdsAsync(display.Id, ct))
    {
        broadcaster.Publish(new PipelineEvent("displayconfig", new { displayId = followerId, config = display.Config }));
    }
}

static async Task<string?> ValidateConfigAsync(DisplayConfig config, IStageRepository stage, CancellationToken ct)
{
    var fonts = await stage.GetFontsAsync(enabledOnly: true, ct);

    async Task<string?> ValidateBackgroundAsync(BackgroundConfig background)
    {
        if (background.Type is not ("solid" or "image" or "motion"))
        {
            return $"Unknown background type '{background.Type}'.";
        }
        if (background.Type is "solid")
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(background.AssetId))
        {
            return "Choose a media asset for an image / motion background.";
        }
        var asset = await stage.GetMediaAssetAsync(background.AssetId, ct);
        if (asset is null)
        {
            return $"Media asset '{background.AssetId}' not found.";
        }
        return asset.Kind == background.Type ? null : $"Media asset is a {asset.Kind}, not a {background.Type}.";
    }

    async Task<string?> ValidateTextAsync(TextDisplayConfig text)
    {
        if (!fonts.Any(f => f.Slug == text.FontSlug))
        {
            return $"Unknown font '{text.FontSlug}'.";
        }
        if (text.FontSizePx is < 24 or > 200)
        {
            return "Font size must be between 24 and 200 px.";
        }
        if (text.FontWeight is < 100 or > 900)
        {
            return "Font weight must be between 100 and 900.";
        }
        if (text.HorizontalAlign is not ("left" or "center" or "right"))
        {
            return $"Unknown horizontal alignment '{text.HorizontalAlign}'.";
        }
        if (text.VerticalAlign is not ("top" or "middle" or "bottom"))
        {
            return $"Unknown vertical alignment '{text.VerticalAlign}'.";
        }
        return await ValidateBackgroundAsync(text.Background);
    }

    if (await ValidateTextAsync(config.Scripture.Text) is { } scriptureError)
    {
        return scriptureError;
    }
    var reference = config.Scripture.Reference;
    if (!fonts.Any(f => f.Slug == reference.FontSlug))
    {
        return $"Unknown font '{reference.FontSlug}'.";
    }
    if (reference.FontSizePx is < 12 or > 96)
    {
        return "Reference font size must be between 12 and 96 px.";
    }
    if (reference.FontWeight is < 100 or > 900)
    {
        return "Font weight must be between 100 and 900.";
    }
    if (!DisplayConfig.IsValidReferencePosition(reference.Position))
    {
        return $"Unknown reference position '{reference.Position}'.";
    }
    if (await ValidateTextAsync(config.Songs.Text) is { } songsError)
    {
        return songsError;
    }
    if (config.Media.Fit is not ("cover" or "contain"))
    {
        return $"Unknown media fit '{config.Media.Fit}'.";
    }
    return null;
}

static DisplayConfig ClampConfig(DisplayConfig config)
{
    static ViewportRect ClampRect(ViewportRect rect)
    {
        var width = Math.Clamp(rect.Width, 10, 100);
        var height = Math.Clamp(rect.Height, 10, 100);
        return new ViewportRect(
            Math.Clamp(rect.X, 0, 100 - width),
            Math.Clamp(rect.Y, 0, 100 - height),
            width, height);
    }

    static TextDisplayConfig ClampText(TextDisplayConfig text) => text with
    {
        Viewport = ClampRect(text.Viewport),
        Padding = new PaddingConfig(
            Math.Clamp(text.Padding.Top, 0, 400),
            Math.Clamp(text.Padding.Right, 0, 400),
            Math.Clamp(text.Padding.Bottom, 0, 400),
            Math.Clamp(text.Padding.Left, 0, 400)),
    };

    return config with
    {
        Scripture = config.Scripture with { Text = ClampText(config.Scripture.Text) },
        Songs = config.Songs with { Text = ClampText(config.Songs.Text) },
        Media = config.Media with { Viewport = ClampRect(config.Media.Viewport) },
    };
}

// --- Audio debugging: device selection, live level meter, session WAV dump ---

api.MapGet("/audio/devices", (IAudioDeviceEnumerator devices, IAudioCapture capture) =>
    Results.Ok(new
    {
        selected = capture.DeviceId,
        devices = devices.ListInputDevices(),
    }));

api.MapPost("/audio/device", (SelectDeviceRequest request, IAudioCapture capture, TranscriptionPipeline pipeline) =>
{
    if (pipeline.IsListening)
    {
        return Results.BadRequest(new { message = "Stop listening before changing the input device." });
    }
    capture.DeviceId = request.DeviceId;
    return Results.Ok();
});

// Poll for a live input level (peak/RMS) and a clipping flag — the meter to eyeball signal health.
api.MapGet("/audio/level", (IAudioCapture capture) =>
{
    var level = capture.CurrentLevel;
    return Results.Ok(new { peak = level.Peak, rms = level.Rms, clipping = level.Peak >= 0.99f });
});

api.MapPost("/audio/recording/{on:bool}", (bool on, SessionRecorder recorder) =>
{
    recorder.Enabled = on;
    return Results.Ok(new { recording = recorder.Enabled });
});

api.MapGet("/audio/recording/latest", (SessionRecorder recorder) =>
    recorder.LastRecordingPath is { } path && File.Exists(path)
        ? Results.File(path, "audio/wav", Path.GetFileName(path))
        : Results.NotFound(new { message = "No recording yet. Enable recording, then start and stop listening." }));

// The engine loads its model lazily on the pipeline's background task, so a missing model
// used to fail *after* this returned 200 — the console showed "Listening: false" with no
// reason. Refuse up front instead, and hand back the engine's own message.
api.MapPost("/listening/start", (TranscriptionPipeline pipeline, ISpeechEngineProvider engines) =>
{
    if (engines.Current.ReadinessError is { } reason)
    {
        return Results.Json(new { message = reason }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    pipeline.Start();
    return Results.Ok();
});

api.MapPost("/listening/stop", async (TranscriptionPipeline pipeline) =>
{
    await pipeline.StopAsync();
    return Results.Ok();
});

api.MapPost("/engine/{name}", async (string name, TranscriptionPipeline pipeline) =>
{
    try
    {
        await pipeline.SwitchEngineAsync(name);
        return Results.Ok();
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
});

// --- Media-file transcription: upload a recording, decode to 16 kHz mono, and run it
//     through the same engine/parser/SSE path as the mic. The original is served back for
//     browser playback so audio and captions play together. ---

var mediaDir = Path.Combine(app.Environment.ContentRootPath, "media");
Directory.CreateDirectory(mediaDir);

api.MapPost("/media/upload", async (HttpRequest request, MediaDecoder decoder) =>
{
    if (!request.HasFormContentType || request.Form.Files.Count == 0)
    {
        return Results.BadRequest(new { message = "Attach an audio file in a multipart form." });
    }
    var file = request.Form.Files[0];

    // Audio only: the pipeline needs the audio track, and video files are needlessly huge.
    // Accept by MIME type or a known audio extension (browsers sometimes omit the type).
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    var isAudio = file.ContentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)
        || extension is ".mp3" or ".m4a" or ".aac" or ".wav" or ".aiff" or ".aif" or ".caf" or ".flac" or ".ogg";
    if (!isAudio)
    {
        return Results.BadRequest(new
        {
            message = "Audio files only. Extract the audio track first " +
                "(e.g. export as MP3 or M4A) — a 1-hour sermon is ~30–60 MB as audio.",
        });
    }

    var id = Guid.NewGuid().ToString("N");
    var originalPath = Path.Combine(mediaDir, id + Path.GetExtension(file.FileName));
    var wavPath = Path.Combine(mediaDir, id + ".decoded.wav");

    await using (var stream = File.Create(originalPath))
    {
        await file.CopyToAsync(stream);
    }
    try
    {
        await decoder.DecodeToWavAsync(originalPath, wavPath, request.HttpContext.RequestAborted);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { message = ex.Message });
    }
    // The playback URL points at the original; transcription reads the decoded WAV.
    return Results.Ok(new { id, name = file.FileName, playbackUrl = $"/api/media/{id}/audio" });
});

api.MapGet("/media/{id}/audio", (string id) =>
{
    var match = Directory.GetFiles(mediaDir, id + ".*").FirstOrDefault(f => !f.EndsWith(".decoded.wav"));
    return match is not null
        ? Results.File(match, enableRangeProcessing: true) // range processing → seekable <audio>
        : Results.NotFound();
});

// fast=true transcribes as fast as the engine allows (no playback sync); default paces
// frames at real time to track browser playback.
api.MapPost("/media/{id}/transcribe", async (
    string id, bool? fast, TranscriptionPipeline pipeline, ISpeechEngineProvider engines) =>
{
    var wavPath = Path.Combine(mediaDir, id + ".decoded.wav");
    if (!File.Exists(wavPath))
    {
        return Results.NotFound(new { message = "Unknown media id. Upload the file again." });
    }
    // Same lazy-load trap as /listening/start: without this the caller gets 200 and the
    // missing model only shows up in the log.
    if (engines.Current.ReadinessError is { } reason)
    {
        return Results.Json(new { message = reason }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    await pipeline.StartFileAsync(wavPath, paced: fast != true);
    return Results.Ok();
});

// --- Background media library: uploaded images / looping videos usable as a display
//     background. Files share the media/ dir (GUID-keyed, no collision with audio); metadata
//     lives in media_assets. Selection rides inside a display's config_json (background.assetId). ---

api.MapGet("/media/backgrounds", async (IStageRepository stage, CancellationToken ct) =>
    Results.Ok(new { assets = await stage.GetMediaAssetsAsync(ct) }));

api.MapPost("/media/backgrounds", async (HttpRequest request, IStageRepository stage) =>
{
    if (!request.HasFormContentType || request.Form.Files.Count == 0)
    {
        return Results.BadRequest(new { message = "Attach an image or video in a multipart form." });
    }
    var file = request.Form.Files[0];
    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

    // Accept by MIME type or a known extension (browsers sometimes omit the type). Still images
    // become 'image' backgrounds; video becomes a looping 'motion' background.
    string? kind = null;
    if (file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
        || extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif")
    {
        kind = "image";
    }
    else if (file.ContentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
        || extension is ".mp4" or ".webm" or ".mov")
    {
        kind = "motion";
    }
    if (kind is null)
    {
        return Results.BadRequest(new
        {
            message = "Images (JPG, PNG, WEBP, GIF) or video (MP4, WEBM, MOV) only.",
        });
    }

    var id = Guid.NewGuid().ToString("N");
    var storedPath = Path.Combine(mediaDir, id + extension);
    await using (var stream = File.Create(storedPath))
    {
        await file.CopyToAsync(stream);
    }

    var title = Path.GetFileNameWithoutExtension(file.FileName);
    if (string.IsNullOrWhiteSpace(title))
    {
        title = kind == "image" ? "Image" : "Motion";
    }
    var asset = await stage.AddMediaAssetAsync(
        new MediaAsset(id, kind, title, extension, file.ContentType, "file", 0));
    return Results.Ok(asset);
});

api.MapGet("/media/backgrounds/{id}/file", async (string id, IStageRepository stage, CancellationToken ct) =>
{
    var asset = await stage.GetMediaAssetAsync(id, ct);
    if (asset is null)
    {
        return Results.NotFound();
    }
    var path = Path.Combine(mediaDir, id + asset.FileExt);
    return File.Exists(path)
        ? Results.File(path, asset.ContentType, enableRangeProcessing: true) // range → seekable/loopable <video>
        : Results.NotFound();
});

api.MapDelete("/media/backgrounds/{id}", async (string id, IStageRepository stage, CancellationToken ct) =>
{
    var asset = await stage.GetMediaAssetAsync(id, ct);
    if (asset is null)
    {
        return Results.NotFound(new { message = "Unknown media id." });
    }
    var path = Path.Combine(mediaDir, id + asset.FileExt);
    if (File.Exists(path))
    {
        File.Delete(path);
    }
    await stage.DeleteMediaAssetAsync(id, ct);
    return Results.Ok();
});

// --- Whisper model A/B: list the installed ggml models and hot-swap between them ---

api.MapGet("/whisper/models", (WhisperSpeechEngine whisper, IWebHostEnvironment env) =>
{
    var dir = Path.Combine(env.ContentRootPath, "models", "whisper");
    var models = Directory.Exists(dir)
        ? Directory.GetFiles(dir, "*.bin").Select(Path.GetFileName).Order().ToArray()
        : [];
    return Results.Ok(new { selected = Path.GetFileName(whisper.ModelPath), models });
});

api.MapPost("/whisper/model", async (
    SelectModelRequest request,
    WhisperSpeechEngine whisper,
    TranscriptionPipeline pipeline,
    IWebHostEnvironment env) =>
{
    var path = Path.Combine(env.ContentRootPath, "models", "whisper", request.Model);
    if (!File.Exists(path))
    {
        return Results.BadRequest(new { message = $"Model '{request.Model}' not found." });
    }
    var wasListening = pipeline.IsListening;
    await pipeline.StopAsync();
    whisper.ModelPath = path;
    if (wasListening)
    {
        pipeline.Start();
    }
    return Results.Ok(new { selected = request.Model });
});

// Parser test input: runs typed text through the same parser/context as live audio.
api.MapPost("/simulate", async (SimulateRequest request, TranscriptionPipeline pipeline) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { message = "Text is required." });
    }
    await pipeline.SimulateUtteranceAsync(request.Text);
    return Results.Ok();
});

// One-way SSE stream: transcript / reference / status / translation / live / displayconfig / pipelineerror events.
app.MapGet("/events", async (HttpContext context, EventBroadcaster broadcaster) =>
{
    context.Response.Headers.ContentType = "text/event-stream";
    context.Response.Headers.CacheControl = "no-cache";
    var (id, reader) = broadcaster.Subscribe();
    try
    {
        await context.Response.WriteAsync(": connected\n\n", context.RequestAborted);
        await context.Response.Body.FlushAsync(context.RequestAborted);
        await foreach (var pipelineEvent in reader.ReadAllAsync(context.RequestAborted))
        {
            var data = JsonSerializer.Serialize(pipelineEvent.Payload, JsonSerializerOptions.Web);
            await context.Response.WriteAsync($"event: {pipelineEvent.Type}\ndata: {data}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    catch (OperationCanceledException)
    {
    }
    finally
    {
        broadcaster.Unsubscribe(id);
    }
});

app.MapFallbackToFile("index.html");

app.Run();

// Shows enough of a stored key for the operator to recognise which one it is, without
// echoing the secret back to the browser.
static string? MaskKey(string? key) =>
    string.IsNullOrWhiteSpace(key) ? null : "\u2026" + key.Trim()[^Math.Min(4, key.Trim().Length)..];

internal sealed record ApiBibleKeyRequest(string? Key);
internal sealed record SimulateRequest(string Text);
internal sealed record SelectDeviceRequest(int? DeviceId);
internal sealed record SelectModelRequest(string Model);
internal sealed record LiveRequest(
    string? Reference,
    string? Text,
    string Translation,
    string? Source,
    string? Book = null,
    int? Chapter = null,
    int? VerseStart = null,
    int? VerseEnd = null,
    string? Kind = null,
    string? MediaId = null,
    string? MediaKind = null,
    bool? MediaLoop = null);
internal sealed record MediaEndedRequest(string Id);
internal sealed record CreateDisplayRequest(string? Name, int? UseSettingsOfDisplayId);
internal sealed record SetDisplaySourceRequest(int? FollowsDisplayId);
