using FirebirdSql.Data.FirebirdClient;
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
    IReadOnlyList<(string Title, string Message)> Errors);

/// <summary>
/// Imports songs from an EasyWorship 6/7 database (Firebird ODS). The two moving parts are
/// kept apart on purpose: <see cref="ReadRows"/> is the thin Firebird-touching reader, while
/// the RTF stripping and section splitting live in the pure, fully-tested
/// <see cref="EasyWorshipMapper"/> — so the risky native dependency is isolated to one method.
///
/// EasyWorship stores the library as song.db (metadata) with each song's lyrics as an RTF
/// blob in the "words" table, keyed by the song id. New titles are inserted via the shared
/// <see cref="ISongRepository"/>; titles already in the library are skipped.
/// </summary>
public sealed class EasyWorshipImporter(ISongRepository songs, ILogger<EasyWorshipImporter> logger)
{
    public async Task<EasyWorshipImportResult> ImportAsync(string songDbPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(songDbPath))
        {
            throw new FileNotFoundException($"EasyWorship database not found: {songDbPath}", songDbPath);
        }

        var rows = ReadRows(songDbPath);

        var existing = new HashSet<string>(
            (await songs.SearchAsync(null, cancellationToken)).Select(s => s.Title),
            StringComparer.OrdinalIgnoreCase);

        var imported = new List<(int, string)>();
        var skipped = new List<string>();
        var errors = new List<(string, string)>();

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (EasyWorshipMapper.Map(row) is not { } draft)
                {
                    errors.Add((row.Title, "No lyrics found."));
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
            "EasyWorship import: {Imported} imported, {Skipped} skipped, {Errors} errors.",
            imported.Count, skipped.Count, errors.Count);
        return new EasyWorshipImportResult(imported, skipped, errors);
    }

    /// <summary>
    /// The single Firebird-specific method: opens the embedded database read-only and reads
    /// song title/author/copyright plus the RTF words blob. Throws
    /// <see cref="InvalidDataException"/> if the file is not a readable EasyWorship song db.
    /// </summary>
    private static IReadOnlyList<EasyWorshipSongRow> ReadRows(string songDbPath)
    {
        var builder = new FbConnectionStringBuilder
        {
            ServerType = FbServerType.Embedded,
            Database = songDbPath,
            UserID = "SYSDBA",
            Password = "masterkey",
            Charset = "NONE",
        };

        var rows = new List<EasyWorshipSongRow>();
        try
        {
            using var connection = new FbConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            // song + word tables joined on the song id; column names match EW 6/7 schema.
            command.CommandText = """
                SELECT s.title, s.author, s.copyright, w.words
                FROM song s
                LEFT JOIN word w ON w.song_id = s.rowid
                ORDER BY s.title
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                rows.Add(new EasyWorshipSongRow(
                    reader.IsDBNull(0) ? string.Empty : reader.GetString(0),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? string.Empty : reader.GetString(3)));
            }
        }
        catch (FbException ex)
        {
            throw new InvalidDataException(
                $"Could not read '{songDbPath}' as an EasyWorship song database. " +
                "Expected an EasyWorship 6/7 database (song.db). " + ex.Message, ex);
        }
        return rows;
    }
}
