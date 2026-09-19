using System.Text;
using LumosPresenter.Core.Songs;
using LumosPresenter.Data.Importing.Paradox;

namespace LumosPresenter.Data.Importing;

/// <summary>
/// Reads song rows out of an EasyWorship 2009 library. The Paradox table holds the metadata
/// columns and a memo pointer per song; the lyrics themselves are RTF living in the sibling
/// .MB blob file. Rows stream lazily so a library of several thousand songs never has to be
/// held in memory at once.
/// </summary>
internal static class ParadoxSongReader
{
    /// <summary>The columns EasyWorship 2009 gives us that map onto a song.</summary>
    private const string TitleColumn = "Title";
    private const string AuthorColumn = "Author";
    private const string CopyrightColumn = "Copyright";
    private const string WordsColumn = "Words";

    public static IEnumerable<EasyWorshipSongRow> ReadRows(EasyWorshipLibrary library)
    {
        using var table = OpenTable(library.SongsPath);
        var title = Require(table, TitleColumn, library.SongsPath);
        var words = Require(table, WordsColumn, library.SongsPath);
        // Author and copyright are optional: some libraries predate them or drop them.
        var author = table.Field(AuthorColumn);
        var copyright = table.Field(CopyrightColumn);

        using var blobs = ParadoxBlobStore.Open(library.WordsPath);

        foreach (var record in table.Records())
        {
            var lyrics = blobs.Read(ParadoxTable.GetBlobRef(record, words));
            yield return new EasyWorshipSongRow(
                ParadoxTable.GetString(record, title),
                author is null ? null : ParadoxTable.GetString(record, author),
                copyright is null ? null : ParadoxTable.GetString(record, copyright),
                // The blob is an RTF document; its own \ansi code page is honoured by the
                // stripper, so decode the bytes one-to-one here and let it do the rest.
                Encoding.Latin1.GetString(lyrics));
        }
    }

    private static ParadoxTable OpenTable(string path)
    {
        try
        {
            return ParadoxTable.Open(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException
                                       or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException(
                $"Could not read '{path}' as an EasyWorship 2009 song table. {ex.Message}", ex);
        }
    }

    private static ParadoxField Require(ParadoxTable table, string name, string path) =>
        table.Field(name)
        ?? throw new InvalidDataException(
            $"'{path}' has no '{name}' column, so it is not an EasyWorship song table.");
}
