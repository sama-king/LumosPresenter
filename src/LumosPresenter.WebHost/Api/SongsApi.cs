using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using LumosPresenter.Core.Songs;
using LumosPresenter.Data.Importing;

namespace LumosPresenter.WebHost.Api;

/// <summary>
/// Song library endpoints under /api/songs: CRUD plus .txt and EasyWorship import. The
/// lyrics parser (<see cref="LyricsParser"/>) is the single splitting truth — the editor
/// submits raw lyrics and the server parses them, so every path (editor, .txt, and
/// EasyWorship's RTF strip → LyricsParser) shares exactly the same section semantics.
///
/// The EasyWorship import reads the library from a path on the host rather than an upload;
/// see the endpoint below for why a file upload cannot express that source.
/// </summary>
internal static class SongsApi
{
    public static RouteGroupBuilder MapSongs(this RouteGroupBuilder api)
    {
        api.MapGet("/songs", async (string? q, ISongRepository songs, CancellationToken ct) =>
            Results.Ok(new { songs = await songs.SearchAsync(q, ct) }));

        api.MapGet("/songs/{id:int}", async (int id, ISongRepository songs, CancellationToken ct) =>
            await songs.GetAsync(id, ct) is { } song
                ? Results.Ok(song)
                : Results.NotFound(new { message = $"Song {id} not found." }));

        api.MapPost("/songs", async (SaveSongRequest request, ISongRepository songs, CancellationToken ct) =>
        {
            if (Build(request) is not { } draft)
            {
                return Results.BadRequest(new { message = "A title and at least one lyric line are required." });
            }
            return Results.Ok(await songs.CreateAsync(draft, ct));
        });

        api.MapPut("/songs/{id:int}", async (
            int id, SaveSongRequest request, ISongRepository songs, CancellationToken ct) =>
        {
            if (Build(request) is not { } draft)
            {
                return Results.BadRequest(new { message = "A title and at least one lyric line are required." });
            }
            return await songs.UpdateAsync(id, draft, ct) is { } song
                ? Results.Ok(song)
                : Results.NotFound(new { message = $"Song {id} not found." });
        });

        api.MapDelete("/songs/{id:int}", async (int id, ISongRepository songs, CancellationToken ct) =>
            await songs.DeleteAsync(id, ct)
                ? Results.Ok(new { })
                : Results.NotFound(new { message = $"Song {id} not found." }));

        // Import one or more .txt files: the filename (sans extension) is the title, the body
        // is parsed with the same blank-line convention as the editor. Partial success is fine —
        // each file reports independently so one bad file doesn't sink the batch.
        api.MapPost("/songs/import/text", async (HttpRequest request, ISongRepository songs, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
            {
                return Results.BadRequest(new { message = "Attach one or more .txt files in a multipart form." });
            }

            var imported = new List<object>();
            var errors = new List<object>();
            foreach (var file in request.Form.Files)
            {
                var name = Path.GetFileName(file.FileName);
                try
                {
                    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                    var isText = file.ContentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                        || extension is ".txt" or "";
                    if (!isText)
                    {
                        errors.Add(new { file = name, message = "Only .txt files are supported." });
                        continue;
                    }

                    using var reader = new StreamReader(file.OpenReadStream());
                    var body = await reader.ReadToEndAsync(ct);
                    var sections = LyricsParser.Parse(body);
                    if (sections.Count == 0)
                    {
                        errors.Add(new { file = name, message = "No lyrics found." });
                        continue;
                    }

                    var title = Path.GetFileNameWithoutExtension(file.FileName);
                    if (string.IsNullOrWhiteSpace(title))
                    {
                        title = "Untitled";
                    }
                    var song = await songs.CreateAsync(
                        new SongDraft(title.Trim(), null, null, sections, "txt"), ct);
                    imported.Add(new { id = song.Id, title = song.Title });
                }
                catch (Exception ex)
                {
                    errors.Add(new { file = name, message = ex.Message });
                }
            }
            return Results.Ok(new { imported, errors });
        });

        // Where is the operator's EasyWorship library? The console asks this first so the
        // common case needs no typing: the profile file EasyWorship writes records the data
        // directory, and the default install locations are probed as a fallback.
        api.MapGet("/songs/import/easyworship/detect", () => Results.Ok(new
        {
            libraries = EasyWorshipSource.Locate().Select(l => new
            {
                path = l.SongsPath,
                format = l.Format.ToString().ToLowerInvariant(),
                description = l.Description,
            }),
        }));

        // Import an EasyWorship library by path. The library is read in place rather than
        // uploaded: EasyWorship 2009 keeps lyrics in a Songs.MB blob file that routinely runs
        // to tens of megabytes, and both generations need a second file beside the first, so
        // a single-file upload cannot express the source. Omit the path to use whatever
        // detection found. Unreadable or missing libraries surface as a 400, not a crash.
        api.MapPost("/songs/import/easyworship", async (
            EasyWorshipImportRequest? request, EasyWorshipImporter importer, CancellationToken ct) =>
        {
            var path = request?.Path;
            if (string.IsNullOrWhiteSpace(path))
            {
                if (EasyWorshipSource.Locate().FirstOrDefault() is not { } detected)
                {
                    return Results.BadRequest(new
                    {
                        message = "No EasyWorship library found. Enter the path to your " +
                            @"EasyWorship 'Databases\Data' folder.",
                    });
                }
                path = detected.SongsPath;
            }

            try
            {
                var result = await importer.ImportAsync(path, ct);
                return Results.Ok(new
                {
                    imported = result.Imported.Select(x => new { id = x.Id, title = x.Title }),
                    skipped = result.Skipped,
                    errors = result.Errors.Select(e => new { title = e.Title, message = e.Message }),
                    source = result.Source,
                });
            }
            catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException
                                           or DirectoryNotFoundException or UnauthorizedAccessException)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        return api;
    }

    /// <summary>Validates and parses a save request into a draft, or null if title/lyrics are empty.</summary>
    private static SongDraft? Build(SaveSongRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return null;
        }
        var sections = LyricsParser.Parse(request.Lyrics ?? string.Empty);
        if (sections.Count == 0)
        {
            return null;
        }
        return new SongDraft(
            request.Title.Trim(),
            string.IsNullOrWhiteSpace(request.Author) ? null : request.Author.Trim(),
            string.IsNullOrWhiteSpace(request.Copyright) ? null : request.Copyright.Trim(),
            sections);
    }
}

internal sealed record SaveSongRequest(string Title, string? Author, string? Copyright, string Lyrics);

/// <summary>
/// Import source: an EasyWorship data folder or songs file. Null means use auto-detection.
/// </summary>
internal sealed record EasyWorshipImportRequest(string? Path);
