namespace LumosPresenter.Core.Parsing;

/// <summary>
/// Cue phrases that signal a scripture reference is being spoken.
///
/// <b>Book cues</b> (<see cref="Phrases"/>) precede a book name ("turn to…", "the book of…").
/// When a book match is immediately preceded by one, it is almost certainly a real reference,
/// so the parser boosts confidence and rescues otherwise-shaky matches (fuzzy spellings, or
/// ambiguous everyday words like "numbers").
///
/// <b>Verse cues</b> (<see cref="VersePhrases"/>) precede a bare verse number without the word
/// "verse" ("start from one", "beginning at three", "pick up at twelve"). Against an
/// established chapter these let a number attach as a verse even mid-utterance, where the
/// plain bare-number guard would otherwise reject it as prose.
///
/// Bare single prepositions ("in", "to") are deliberately excluded — too common in ordinary
/// speech to be reliable cues. Also reusable as ASR bias vocabulary (see <see cref="BiasVocabulary"/>).
/// </summary>
public static class ReferenceCues
{
    /// <summary>
    /// Book-cue phrases as ordered word sequences (lowercase, no punctuation), longest first so
    /// the parser prefers the most specific match ("turn with me to" over "to").
    /// </summary>
    public static IReadOnlyList<string[]> Phrases { get; }

    /// <summary>The maximum book-cue length in words, so the parser knows how far back to look.</summary>
    public static int MaxWords { get; }

    /// <summary>Verse-cue phrases (longest first), for "start from N" / "begin at N" constructions.</summary>
    public static IReadOnlyList<string[]> VersePhrases { get; }

    /// <summary>The maximum verse-cue length in words.</summary>
    public static int VerseMaxWords { get; }

    private static readonly string[] RawPhrases =
    [
        // Turn / navigation
        "turn to", "turn with me to", "turn over to", "turn in your bibles to",
        "let's turn to", "let us turn to", "go to", "let's go to", "let us go to",
        "flip to", "flip over to", "open to", "open your bibles to", "open up to",
        // "book of"
        "the book of", "book of", "from the book of", "in the book of", "to the book of",
        // Reading / looking
        "let's read", "let us read", "reading from", "read from", "we read in",
        "look at", "let's look at", "looking at", "let's look in",
        // Location / found-in
        "found in", "it's in", "over in", "back in", "we're in", "we are in",
        "here in", "this is in",
        // Reference / consideration
        "according to", "as it says in", "it says in", "we see in", "we find in",
        "let's consider",
    ];

    private static readonly string[] RawVersePhrases =
    [
        // "start" family — "we'll start from one", "start at verse three", "starting from twelve"
        "start from", "start at", "start in", "starting from", "starting at",
        "we'll start from", "we'll start at", "let's start at", "let's start from",
        // "begin" family
        "begin at", "begin from", "beginning at", "beginning from", "beginning in",
        // "pick up" family
        "pick up at", "pick up in", "pick up from", "picking up at",
        // navigation to a verse
        "drop down to", "down to", "from verse", "at verse", "in verse",
        "read from", "reading from", "look at",
    ];

    static ReferenceCues()
    {
        static string[][] Build(string[] raw) => raw
            .Select(p => p.Replace("'", "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .OrderByDescending(words => words.Length)
            .ToArray();

        Phrases = Build(RawPhrases);
        MaxWords = Phrases.Max(p => p.Length);
        VersePhrases = Build(RawVersePhrases);
        VerseMaxWords = VersePhrases.Max(p => p.Length);
    }
}
