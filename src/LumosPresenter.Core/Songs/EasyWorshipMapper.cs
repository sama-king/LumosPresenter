using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Songs;

/// <summary>
/// A single song row read from an EasyWorship database, before normalization. Words is the
/// raw RTF lyric blob. This record is the seam between the Firebird-specific reader (Data
/// project) and the pure mapping below, so the mapping can be unit-tested without Firebird.
/// </summary>
public sealed record EasyWorshipSongRow(string Title, string? Author, string? Copyright, string Words);

/// <summary>
/// Normalizes EasyWorship song rows into <see cref="SongDraft"/>s: strip the RTF lyrics to
/// plain text, then split into sections with the shared <see cref="LyricsParser"/> so imports
/// slice into slides exactly like editor and .txt songs.
/// </summary>
public static class EasyWorshipMapper
{
    public static SongDraft? Map(EasyWorshipSongRow row)
    {
        var title = row.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }
        var lyrics = RtfStripper.ToPlainText(row.Words ?? string.Empty);
        var sections = LyricsParser.Parse(lyrics);
        if (sections.Count == 0)
        {
            return null;
        }
        return new SongDraft(
            title,
            string.IsNullOrWhiteSpace(row.Author) ? null : row.Author.Trim(),
            string.IsNullOrWhiteSpace(row.Copyright) ? null : row.Copyright.Trim(),
            sections,
            "easyworship");
    }
}
