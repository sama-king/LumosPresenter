using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using LumosPresenter.Core.Songs;
using LumosPresenter.Data.Importing;

namespace LumosPresenter.WebHost.Api;

/// <summary>
/// Song library endpoints under /api/songs: CRUD plus .txt import. The lyrics parser
/// (<see cref="LyricsParser"/>) is the single splitting truth — the editor submits raw
/// lyrics and the server parses them, so the .txt path and a future EasyWorship import
/// (POST /api/songs/import/easyworship: multipart 'songs' + 'songwords' Firebird files →
/// RTF strip → LyricsParser) share exactly the same section semantics.
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

        // Import an EasyWorship 6/7 song database (song.db). The uploaded file is spooled to a
        // temp path, read via Firebird, mapped through the shared RTF strip + lyrics parser, and
        // titles already in the library are skipped. Firebird embedded needs native binaries at
        // runtime; unreadable files surface as a 400 rather than a crash.
        api.MapPost("/songs/import/easyworship", async (
            HttpRequest request, EasyWorshipImporter importer, CancellationToken ct) =>
        {
            if (!request.HasFormContentType || request.Form.Files.Count == 0)
            {
                return Results.BadRequest(new { message = "Attach an EasyWorship song.db file." });
            }
            var file = request.Form.Files[0];
            var tempPath = Path.Combine(Path.GetTempPath(), $"ew-import-{Guid.NewGuid():N}.db");
            try
            {
                await using (var stream = File.Create(tempPath))
                {
                    await file.CopyToAsync(stream, ct);
                }
                var result = await importer.ImportAsync(tempPath, ct);
                return Results.Ok(new
                {
                    imported = result.Imported.Select(x => new { id = x.Id, title = x.Title }),
                    skipped = result.Skipped,
                    errors = result.Errors.Select(e => new { title = e.Title, message = e.Message }),
                });
            }
            catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
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
