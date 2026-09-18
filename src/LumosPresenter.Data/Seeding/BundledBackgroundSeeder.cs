using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LumosPresenter.Data.Seeding;

/// <summary>
/// Registers the default backgrounds that ship with the app, so a fresh install opens with
/// something to choose from rather than an empty background picker.
///
/// The images come from <c>assets/backgrounds/</c> in the repo, which the WebHost project
/// links into its build and publish output as a <c>backgrounds/</c> folder beside the exe.
/// On startup each one is copied into the media directory and given a <c>media_assets</c>
/// row with source 'bundled' — the same shape as an uploaded background, so every existing
/// path (listing, serving, deleting, selecting for a display) works on it unchanged.
///
/// Three situations have to be told apart, and a ledger in app settings is what separates
/// them, because the rows alone cannot:
/// <list type="bullet">
///   <item>Never seeded — copy the file and add the row. This is also how a new default
///   shipped in a later release reaches an existing install.</item>
///   <item>Seeded, then deleted by the operator — leave it deleted. Without the ledger it
///   would reappear at every startup.</item>
///   <item>Row present but its file missing — restore the file. The package ships a copy
///   of the build machine's database but not its media directory, so a row can arrive in a
///   fresh install with nothing on disk behind it.</item>
/// </list>
/// </summary>
public sealed class BundledBackgroundSeeder(
    IStageRepository stage,
    IAppSettings settings,
    IHostEnvironment environment,
    ILogger<BundledBackgroundSeeder> logger) : IHostedService
{
    /// <summary>Folder beside the exe that holds the shipped backgrounds.</summary>
    public const string BundledDirectoryName = "backgrounds";

    /// <summary>Id prefix for seeded assets, keeping them apart from uploads' GUID ids.</summary>
    internal const string IdPrefix = "bundled-";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // The source is read from beside the exe (AppContext.BaseDirectory), which is where
        // build and publish put linked content. The destination follows Program.cs, which
        // keeps backgrounds in the content root's media/ folder — in the installed app the
        // two are the same directory; under `dotnet run` they are not.
        var source = Path.Combine(AppContext.BaseDirectory, BundledDirectoryName);
        var media = Path.Combine(environment.ContentRootPath, "media");
        try
        {
            await SeedAsync(source, media, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Defaults are a convenience: failing to register them must not keep the
            // operator from running a service.
            logger.LogWarning(ex, "Could not register bundled backgrounds from {Source}.", source);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Brings the media library in line with the bundled folder. Returns how many
    /// backgrounds were newly added (restored files are not counted).
    /// </summary>
    internal async Task<int> SeedAsync(
        string sourceDirectory, string mediaDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            logger.LogDebug("No bundled backgrounds folder at {Source}.", sourceDirectory);
            return 0;
        }

        var ledgerValue = settings.Get(AppSettingKeys.BundledBackgroundsSeeded);
        var ledger = new HashSet<string>(
            (ledgerValue ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries),
            StringComparer.Ordinal);
        var ledgerChanged = false;

        Directory.CreateDirectory(mediaDirectory);
        var added = 0;

        // Ordered so a fresh install lists the defaults alphabetically.
        foreach (var file in Directory.EnumerateFiles(sourceDirectory).Order(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (Classify(extension) is not { } classified)
            {
                continue;
            }
            var (kind, contentType) = classified;

            var name = Path.GetFileNameWithoutExtension(file);
            var id = IdPrefix + Slug(name);
            var target = Path.Combine(mediaDirectory, id + extension);

            if (await stage.GetMediaAssetAsync(id, cancellationToken) is { } existing)
            {
                if (existing.FileExt == extension && !File.Exists(target))
                {
                    File.Copy(file, target, overwrite: true);
                    logger.LogInformation("Restored missing file for bundled background {Id}.", id);
                }
                ledgerChanged |= ledger.Add(id);
                continue;
            }

            if (ledger.Contains(id))
            {
                continue; // registered once and since deleted by the operator
            }

            File.Copy(file, target, overwrite: true);
            await stage.AddMediaAssetAsync(
                new MediaAsset(id, kind, Title(name), extension, contentType, "bundled", 0),
                cancellationToken);
            ledger.Add(id);
            ledgerChanged = true;
            added++;
        }

        if (ledgerChanged)
        {
            settings.Set(AppSettingKeys.BundledBackgroundsSeeded, string.Join(',', ledger.Order(StringComparer.Ordinal)));
        }
        if (added > 0)
        {
            logger.LogInformation("Registered {Count} bundled background(s).", added);
        }
        return added;
    }

    /// <summary>
    /// The same split the upload endpoint makes: stills become 'image' backgrounds, video
    /// becomes a looping 'motion' one. Anything else in the folder is ignored.
    /// </summary>
    private static (string Kind, string ContentType)? Classify(string extension) => extension switch
    {
        ".jpg" or ".jpeg" => ("image", "image/jpeg"),
        ".png" => ("image", "image/png"),
        ".webp" => ("image", "image/webp"),
        ".gif" => ("image", "image/gif"),
        ".mp4" => ("motion", "video/mp4"),
        ".webm" => ("motion", "video/webm"),
        ".mov" => ("motion", "video/quicktime"),
        _ => null,
    };

    /// <summary>
    /// A stable id from the filename. It has to be stable, not random: the ledger and the
    /// restore check both recognise a default across restarts and releases by its id.
    /// </summary>
    internal static string Slug(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-');
        var slug = string.Join('-', new string(chars.ToArray()).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "background" : slug;
    }

    /// <summary>"mountain-lake-reflection" → "Mountain Lake Reflection".</summary>
    internal static string Title(string name) => string.Join(' ',
        name.Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
}
