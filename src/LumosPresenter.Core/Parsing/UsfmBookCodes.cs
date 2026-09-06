namespace LumosPresenter.Core.Parsing;

/// <summary>
/// Maps canonical book numbers (1..66, as in <see cref="BookCatalog"/>) to the USFM
/// three-letter codes remote scripture APIs address chapters with ("JHN.3"). Keyed on
/// book number rather than name so it cannot drift from the catalog: the constructor
/// verifies the two agree in length at startup.
/// </summary>
public static class UsfmBookCodes
{
    private static readonly string[] Codes =
    [
        "GEN", "EXO", "LEV", "NUM", "DEU", "JOS", "JDG", "RUT", "1SA", "2SA",
        "1KI", "2KI", "1CH", "2CH", "EZR", "NEH", "EST", "JOB", "PSA", "PRO",
        "ECC", "SNG", "ISA", "JER", "LAM", "EZK", "DAN", "HOS", "JOL", "AMO",
        "OBA", "JON", "MIC", "NAM", "HAB", "ZEP", "HAG", "ZEC", "MAL",
        "MAT", "MRK", "LUK", "JHN", "ACT", "ROM", "1CO", "2CO", "GAL", "EPH",
        "PHP", "COL", "1TH", "2TH", "1TI", "2TI", "TIT", "PHM", "HEB", "JAS",
        "1PE", "2PE", "1JN", "2JN", "3JN", "JUD", "REV",
    ];

    static UsfmBookCodes()
    {
        if (Codes.Length != BookCatalog.Books.Count)
        {
            throw new InvalidOperationException(
                $"USFM code table has {Codes.Length} entries but BookCatalog has {BookCatalog.Books.Count} books.");
        }
    }

    /// <summary>The USFM code for a canonical book number, or null if out of range.</summary>
    public static string? ForBookNumber(int bookNumber) =>
        bookNumber >= 1 && bookNumber <= Codes.Length ? Codes[bookNumber - 1] : null;

    /// <summary>The canonical book number for a USFM code, or null if unrecognized.</summary>
    public static int? ToBookNumber(string usfmCode)
    {
        var index = Array.FindIndex(Codes, c => c.Equals(usfmCode, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index + 1 : null;
    }
}
