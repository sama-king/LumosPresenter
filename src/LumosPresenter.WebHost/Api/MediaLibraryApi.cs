using LumosPresenter.Core.Abstractions;

namespace LumosPresenter.WebHost.Api;

/// <summary>
/// Media gallery endpoints under /api/media/library. Files are linked from where they already
/// live, never copied, so the console has to name a path on this machine — which a browser
/// cannot read out of a file input or a drop. The /browse endpoint closes that gap: the
/// WebHost and the console run on the same machine, so the server lists directories and the
/// operator picks from them, and every stored path originates server-side.
///
/// Security: the file endpoint resolves its path from the database row alone. A path from the
/// query string is never opened, so a linked gallery is not a read-anything hole.
/// </summary>
internal static class MediaLibraryApi
{
    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif"];

    private static readonly string[] VideoExtensions =
        [".mp4", ".webm", ".mov", ".m4v", ".mkv"];

    public static RouteGroupBuilder MapMediaLibrary(this RouteGroupBuilder api)
    {
        api.MapGet("/media/library", async (IMediaLibraryRepository library, CancellationToken ct) =>
            Results.Ok(new { items = await library.GetAllAsync(ct) }));

        // Links one or more absolute paths. Partial success: each path reports independently, so
        // a folder holding one unsupported file still adds the rest.
        api.MapPost("/media/library", async (
            AddMediaRequest request, IMediaLibraryRepository library, CancellationToken ct) =>
        {
            if (request.Paths is null || request.Paths.Count == 0)
            {
                return Results.BadRequest(new { message = "Give one or more file paths to link." });
            }

            var added = new List<object>();
            var errors = new List<object>();
            foreach (var raw in request.Paths)
            {
                var path = NormalizePath(raw);
                var name = Path.GetFileName(path);
                if (path is "" or null)
                {
                    errors.Add(new { file = raw, message = "Empty path." });
                    continue;
                }
                if (!Path.IsPathRooted(path))
                {
                    errors.Add(new { file = name, message = "An absolute path is required." });
                    continue;
                }
                if (!File.Exists(path))
                {
                    errors.Add(new { file = name, message = "File not found." });
                    continue;
                }
                if (ClassifyKind(path) is not { } kind)
                {
                    errors.Add(new { file = name, message = "Unsupported file type." });
                    continue;
                }
                try
                {
                    var title = Path.GetFileNameWithoutExtension(path);
                    var item = await library.AddAsync(
                        path, kind,
                        string.IsNullOrWhiteSpace(title) ? name : title,
                        ContentTypeFor(path), ct);
                    added.Add(item);
                }
                catch (Exception ex)
                {
                    errors.Add(new { file = name, message = ex.Message });
                }
            }
            return Results.Ok(new { added, errors });
        });

        // Streams the linked file straight from its source path. Range processing keeps video
        // seekable; a missing file is a 404, which is what drives the console's error thumbnail.
        api.MapGet("/media/library/{id}/file", async (
            string id, IMediaLibraryRepository library, CancellationToken ct) =>
        {
            var item = await library.GetAsync(id, ct);
            if (item is null || !File.Exists(item.SourcePath))
            {
                return Results.NotFound();
            }
            return Results.File(item.SourcePath, item.ContentType, enableRangeProcessing: true);
        });

        api.MapDelete("/media/library/{id}", async (
            string id, IMediaLibraryRepository library, CancellationToken ct) =>
            await library.DeleteAsync(id, ct)
                ? Results.Ok(new { })
                : Results.NotFound(new { message = "Unknown media id." }));

        // Server-side file browser backing the "Add from disk" dialog. Lists directories and
        // supported media files only; with no path it starts at the operator's home directory.
        api.MapGet("/media/library/browse", (string? path) =>
        {
            var target = string.IsNullOrWhiteSpace(path)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : NormalizePath(path);

            if (!Directory.Exists(target))
            {
                return Results.BadRequest(new { message = $"Folder not found: {target}" });
            }

            try
            {
                var directories = new DirectoryInfo(target)
                    .EnumerateDirectories()
                    .Where(d => !d.Attributes.HasFlag(FileAttributes.Hidden) && !d.Name.StartsWith('.'))
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new { name = d.Name, path = d.FullName })
                    .ToList();

                var files = new DirectoryInfo(target)
                    .EnumerateFiles()
                    .Where(f => ClassifyKind(f.FullName) is not null)
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new
                    {
                        name = f.Name,
                        path = f.FullName,
                        kind = ClassifyKind(f.FullName),
                        size = f.Length,
                    })
                    .ToList();

                return Results.Ok(new
                {
                    path = target,
                    parent = Directory.GetParent(target)?.FullName,
                    directories,
                    files,
                });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.BadRequest(new { message = "That folder can't be read." });
            }
        });

        return api;
    }

    /// <summary>
    /// Accepts a plain path or a file:// URL — a Finder/Explorer drop hands the browser the
    /// latter, and that is the one case where a drop does reveal a real path.
    /// </summary>
    private static string NormalizePath(string raw)
    {
        var value = raw.Trim();
        if (value.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            && Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return uri.LocalPath;
        }
        return value;
    }

    /// <summary>'image' | 'video' by extension, or null when the file is neither.</summary>
    private static string? ClassifyKind(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (ImageExtensions.Contains(extension)) return "image";
        if (VideoExtensions.Contains(extension)) return "video";
        return null;
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".bmp" => "image/bmp",
        ".avif" => "image/avif",
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        _ => "application/octet-stream",
    };
}

internal sealed record AddMediaRequest(IReadOnlyList<string>? Paths);
