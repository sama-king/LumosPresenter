using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Songs;
using Microsoft.Extensions.Logging;

namespace LumosPresenter.Data.Importing;

/// <summary>
/// Outcome of an EasyWorship import: what landed, what was skipped as an existing title,
/// and per-song errors. Mirrors the .txt import's partial-success contract.
/// </summary>
public sealed record EasyWorshipImportResult(
    IReadOnlyList<(int Id, string Title)> Imported,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<(string Title, string Message)> Errors,
    string Source);

/// <summary>
/// Imports songs from an EasyWorship library, whichever generation it is. The format-specific
/// readers are kept apart from the mapping on purpose: each reader's only job is to produce
/// <see cref="EasyWorshipSongRow"/> values, while the RTF stripping and section splitting live
/// in the pure, fully-tested <see cref="EasyWorshipMapper"/> — so both formats slice into
/// slides exactly like editor and .txt songs, and the risky file parsing stays isolated.
///
/// New titles are inserted through the shared <see cref="ISongRepository"/>; titles already in
/// the library are skipped, so re-running an import is safe.
/// </summary>
public sealed class EasyWorshipImporter(ISongRepository songs, ILogger<EasyWorshipImporter> logger)
{
    /// <summary>Resolves a path to a library, then imports it.</summary>
    public async Task<EasyWorshipImportResult> ImportAsync(
        string path, CancellationToken cancellationToken = default)
    {
        if (!EasyWorshipSource.TryResolve(path, out var library, out var error))
        {
            throw new InvalidDataException(error);
        }
        return await ImportAsync(library, cancellationToken);
    }

    public async Task<EasyWorshipImportResult> ImportAsync(
        EasyWorshipLibrary library, CancellationToken cancellationToken = default)
    {
        var existing = new HashSet<string>(
            (await songs.SearchAsync(null, cancellationToken)).Select(s => s.Title),
            StringComparer.OrdinalIgnoreCase);

        var imported = new List<(int, string)>();
        var skipped = new List<string>();
        var errors = new List<(string, string)>();

        foreach (var row in ReadRows(library))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (EasyWorshipMapper.Map(row) is not { } draft)
                {
                    // A blank title has nothing to report against; skip it silently rather
                    // than filling the operator's error list with nameless rows.
                    if (!string.IsNullOrWhiteSpace(row.Title))
                    {
                        errors.Add((row.Title.Trim(), "No lyrics found."));
                    }
                    continue;
                }
                if (!existing.Add(draft.Title))
                {
                    skipped.Add(draft.Title);
                    continue;
                }
                var song = await songs.CreateAsync(draft, cancellationToken);
                imported.Add((song.Id, song.Title));
            }
            catch (Exception ex)
            {
                errors.Add((row.Title, ex.Message));
            }
        }

        logger.LogInformation(
            "EasyWorship import from {Path} ({Format}): {Imported} imported, {Skipped} skipped, {Errors} errors.",
            library.SongsPath, library.Format, imported.Count, skipped.Count, errors.Count);

        return new EasyWorshipImportResult(imported, skipped, errors, library.Description);
    }

    private static IEnumerable<EasyWorshipSongRow> ReadRows(EasyWorshipLibrary library) =>
        library.Format switch
        {
            EasyWorshipFormat.Paradox => ParadoxSongReader.ReadRows(library),
            EasyWorshipFormat.Firebird => FirebirdSongReader.ReadRows(library),
            _ => throw new InvalidDataException($"Unsupported EasyWorship format '{library.Format}'."),
        };
}
