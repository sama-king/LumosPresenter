namespace LumosPresenter.Core.Parsing;

/// <summary>
/// Cue phrases that signal the speaker is naming a translation to read from
/// ("switch to the…", "reading from the…", "in the King James"). Short letter codes
/// ("KJV", spelled "K J V") are too easy to mis-hear or mention in passing, so
/// <see cref="TranslationDetector"/> only accepts them when one of these phrases
/// immediately precedes; distinctive multi-word names match without a cue.
/// </summary>
public static class TranslationCues
{
    /// <summary>Cue phrases as ordered word sequences (lowercase, no punctuation), longest first.</summary>
    public static IReadOnlyList<string[]> Phrases { get; }

    private static readonly string[] RawPhrases =
    [
        // Switching
        "switch to", "switch to the", "switch over to", "switch over to the",
        "change to", "change to the", "change it to", "change it to the",
        "let's switch to", "let's switch to the",
        // Using / reading
        "using the", "use the", "let's use", "let's use the", "let us use the",
        "read from the", "reading from the", "read in the", "reading in the",
        "read it in the", "let's read from the",
        // Location / source
        "in the", "from the", "out of the", "according to the", "in your",
    ];

    static TranslationCues()
    {
        Phrases = RawPhrases
            .Select(p => p.Replace("'", "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .OrderByDescending(words => words.Length)
            .ToArray();
    }
}
