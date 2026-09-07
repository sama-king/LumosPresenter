using System.Text;
using FirebirdSql.Data.FirebirdClient;
using LumosPresenter.Core.Songs;

namespace LumosPresenter.Data.Importing;

/// <summary>
/// Reads song rows out of an EasyWorship 6/7 library. Unlike the 2009 format, 6/7 keeps the
/// song metadata and the lyrics in two <em>separate</em> Firebird databases — Songs.db holds
/// the "song" table, SongWords.db the "word" table — so they cannot be joined in one query.
/// Both are read independently and joined in memory on the song id.
///
/// This is the only Firebird-touching code in the import path; everything downstream works on
/// plain <see cref="EasyWorshipSongRow"/> values.
/// </summary>
internal static class FirebirdSongReader
{
    public static IReadOnlyList<EasyWorshipSongRow> ReadRows(EasyWorshipLibrary library)
    {
        var lyrics = ReadLyrics(library.WordsPath!);

        var rows = new List<EasyWorshipSongRow>();
        Query(library.SongsPath, "SELECT s.rowid, s.title, s.author, s.copyright FROM song s ORDER BY s.title",
            reader =>
            {
                var id = reader.GetInt32(0);
                rows.Add(new EasyWorshipSongRow(
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    lyrics.GetValueOrDefault(id, string.Empty)));
            },
            "song");
        return rows;
    }

    /// <summary>song id → RTF lyrics, read from the separate SongWords.db.</summary>
    private static Dictionary<int, string> ReadLyrics(string wordsPath)
    {
        var lyrics = new Dictionary<int, string>();
        Query(wordsPath, "SELECT w.song_id, w.words FROM word w",
            reader =>
            {
                if (reader.IsDBNull(0))
                {
                    return;
                }
                lyrics[reader.GetInt32(0)] = reader.IsDBNull(1) ? string.Empty : ReadText(reader, 1);
            },
            "word");
        return lyrics;
    }

    /// <summary>
    /// The lyrics column is a text blob. Read with charset NONE the driver may hand back
    /// either a string or the raw bytes, so accept both rather than assuming one.
    /// </summary>
    private static string ReadText(FbDataReader reader, int ordinal) => reader.GetValue(ordinal) switch
    {
        string text => text,
        byte[] bytes => Encoding.Latin1.GetString(bytes),
        var other => other?.ToString() ?? string.Empty,
    };

    private static void Query(string databasePath, string sql, Action<FbDataReader> onRow, string table)
    {
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException($"EasyWorship database not found: {databasePath}", databasePath);
        }

        var builder = new FbConnectionStringBuilder
        {
            ServerType = FbServerType.Embedded,
            Database = databasePath,
            UserID = "SYSDBA",
            Password = "masterkey",
            Charset = "NONE",
        };

        try
        {
            using var connection = new FbConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                onRow(reader);
            }
        }
        catch (FbException ex)
        {
            throw new InvalidDataException(
                $"Could not read '{databasePath}' as an EasyWorship 6/7 database " +
                $"(expected a '{table}' table). {ex.Message}", ex);
        }
    }
}
