using System.Globalization;
using System.Text;

namespace LumosPresenter.Core.Songs;

/// <summary>
/// Minimal RTF → plain text conversion for imported lyrics (EasyWorship stores each song's
/// words as RTF). This is deliberately small: it handles the control words that appear in
/// lyric bodies — paragraph/line breaks, tabs, unicode and hex escapes — and skips header
/// destinations (font/colour tables) rather than implementing the full RTF spec.
/// </summary>
public static class RtfStripper
{
    public static string ToPlainText(string rtf)
    {
        if (string.IsNullOrEmpty(rtf))
        {
            return string.Empty;
        }

        var output = new StringBuilder();
        // Destinations we skip wholesale (their content is metadata, not lyrics).
        var skipDepths = new Stack<int>();
        var groupDepth = 0;
        var i = 0;

        bool Skipping() => skipDepths.Count > 0;

        while (i < rtf.Length)
        {
            var c = rtf[i];
            switch (c)
            {
                case '{':
                    groupDepth++;
                    i++;
                    break;

                case '}':
                    if (skipDepths.Count > 0 && skipDepths.Peek() == groupDepth)
                    {
                        skipDepths.Pop();
                    }
                    groupDepth--;
                    i++;
                    break;

                case '\\':
                    i = HandleControl(rtf, i, output, skipDepths, groupDepth, Skipping());
                    break;

                case '\r':
                case '\n':
                    i++; // raw newlines in the RTF stream are not content
                    break;

                default:
                    if (!Skipping())
                    {
                        output.Append(c);
                    }
                    i++;
                    break;
            }
        }

        // Collapse the runs of blank lines the control words may have produced, and trim.
        return string.Join('\n',
            output.ToString().Replace("\r\n", "\n").Split('\n').Select(line => line.TrimEnd()))
            .Trim();
    }

    /// <summary>Consumes one backslash control sequence; returns the index just past it.</summary>
    private static int HandleControl(
        string rtf, int start, StringBuilder output, Stack<int> skipDepths, int groupDepth, bool skipping)
    {
        var i = start + 1;
        if (i >= rtf.Length)
        {
            return i;
        }

        var next = rtf[i];

        // Escaped literal characters: \{ \} \\
        if (next is '{' or '}' or '\\')
        {
            if (!skipping)
            {
                output.Append(next);
            }
            return i + 1;
        }

        // \* marks an ignorable destination group — skip its entire group.
        if (next == '*')
        {
            skipDepths.Push(groupDepth);
            return i + 1;
        }

        // \'hh — a byte in hex (code page). Decode as Windows-1252, the EasyWorship default.
        if (next == '\'' && i + 2 < rtf.Length)
        {
            if (byte.TryParse(rtf.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
                && !skipping)
            {
                output.Append(DecodeByte(b));
            }
            return i + 3;
        }

        // A control word: letters, then an optional signed numeric parameter.
        if (char.IsLetter(next))
        {
            var wordStart = i;
            while (i < rtf.Length && char.IsLetter(rtf[i]))
            {
                i++;
            }
            var word = rtf[wordStart..i];

            var numStart = i;
            if (i < rtf.Length && (rtf[i] == '-' || char.IsDigit(rtf[i])))
            {
                i++;
                while (i < rtf.Length && char.IsDigit(rtf[i]))
                {
                    i++;
                }
            }
            var hasParam = i > numStart;
            var param = hasParam ? int.Parse(rtf.AsSpan(numStart, i - numStart), CultureInfo.InvariantCulture) : 0;

            // A single trailing space is the delimiter and is swallowed.
            if (i < rtf.Length && rtf[i] == ' ')
            {
                i++;
            }

            // Header destinations whose content is metadata, not lyrics. Guard against a double
            // push when the group is already marked ignorable (e.g. "{\*\generator ...}").
            if (word is "fonttbl" or "colortbl" or "stylesheet" or "info" or "generator" or "pict")
            {
                if (skipDepths.Count == 0 || skipDepths.Peek() != groupDepth)
                {
                    skipDepths.Push(groupDepth);
                }
            }
            else if (!skipping)
            {
                switch (word)
                {
                    case "par":
                    case "line":
                    case "sect":
                        output.Append('\n');
                        break;
                    case "tab":
                        output.Append('\t');
                        break;
                    case "u" when hasParam:
                        // \uN — a UTF-16 code unit (may be negative for values > 32767).
                        output.Append((char)(param < 0 ? param + 65536 : param));
                        break;
                }
            }
            return i;
        }

        // Any other escaped symbol: skip the backslash and the char.
        return i + 1;
    }

    // Windows-1252 is ASCII for 0x00–0x7F and matches Latin-1 (Unicode code point == byte) for
    // 0xA0–0xFF. The 0x80–0x9F range differs from Latin-1; map the few that show up in lyrics
    // (curly quotes, dashes, ellipsis) explicitly and pass everything else through as-is.
    private static char DecodeByte(byte b) => b switch
    {
        0x91 => '‘', // ‘
        0x92 => '’', // ’
        0x93 => '“', // “
        0x94 => '”', // ”
        0x95 => '•', // •
        0x96 => '–', // –
        0x97 => '—', // —
        0x85 => '…', // …
        _ => (char)b,
    };
}
