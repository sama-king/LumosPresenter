using System.Text;
using System.Text.RegularExpressions;
using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Songs;

/// <summary>
/// The single parsing truth for song lyrics: the in-app editor, .txt import, and (after
/// RTF stripping) the EasyWorship import all feed raw text through here. A blank line
/// starts a new section (slide); a block whose first line is a bare section name —
/// "Verse 1", "Chorus", "Bridge:" — becomes that section's label.
/// </summary>
public static partial class LyricsParser
{
    [GeneratedRegex(
        @"^(verse|chorus|pre[- ]?chorus|bridge|tag|intro|outro|ending|refrain|vamp)(\s+\d+)?\s*:?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LabelLine();

    /// <summary>Splits raw lyrics into ordered sections. Empty and label-only blocks are dropped.</summary>
    public static IReadOnlyList<SongSection> Parse(string lyrics)
    {
        if (string.IsNullOrWhiteSpace(lyrics))
        {
            return [];
        }

        var sections = new List<SongSection>();
        // Normalize newlines, then split on runs of blank lines (whitespace-only counts as blank).
        var blocks = Regex.Split(lyrics.Replace("\r\n", "\n").Replace('\r', '\n'), @"\n\s*\n");
        foreach (var block in blocks)
        {
            var lines = block.Split('\n')
                .Select(line => line.TrimEnd())
                .SkipWhile(string.IsNullOrWhiteSpace)
                .ToList();
            string? label = null;
            if (lines.Count > 0 && LabelLine().IsMatch(lines[0].Trim()))
            {
                label = NormalizeLabel(lines[0]);
                lines.RemoveAt(0);
            }
            var text = string.Join('\n', lines).Trim();
            if (text.Length > 0)
            {
                sections.Add(new SongSection(sections.Count, label, text));
            }
        }
        return sections;
    }

    /// <summary>Sections back to editor text — the deterministic inverse of <see cref="Parse"/>.</summary>
    public static string Compose(IReadOnlyList<SongSection> sections)
    {
        var builder = new StringBuilder();
        foreach (var section in sections)
        {
            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }
            if (section.Label is { } label)
            {
                builder.Append(label).Append('\n');
            }
            builder.Append(section.Text);
        }
        return builder.ToString();
    }

    /// <summary>"chorus:" → "Chorus", "verse  2" → "Verse 2", "PRE-CHORUS" → "Pre-Chorus".</summary>
    private static string NormalizeLabel(string line)
    {
        var words = line.Trim().TrimEnd(':').Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words.Select(word =>
            string.Join('-', word.Split('-').Select(part =>
                part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()))));
    }
}
