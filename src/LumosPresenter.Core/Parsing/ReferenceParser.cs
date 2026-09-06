using LumosPresenter.Core.Domain;

namespace LumosPresenter.Core.Parsing;

/// <summary>
/// Detects Bible references in transcribed speech. Pure logic, no I/O — validated by
/// the xUnit corpus in LumosPresenter.Tests.
///
/// Handles book aliases and mis-transcriptions (via <see cref="BookCatalog"/>), spoken
/// numbers ("three sixteen", "one hundred and nineteen"), explicit grammar in either
/// order ("chapter three verse sixteen", "the fifth chapter", "verse twelve", "3:16"),
/// verse ranges ("sixteen through eighteen"), reversed psalms ("the twenty-third psalm"),
/// and — crucially for live preaching — references that arrive across several utterances
/// with filler in between:
///
///   "turn to Ephesians" … "let's go to the fifth chapter" … "verse twelve"  →  Ephesians 5:12
///
/// This is handled by a <b>sticky context</b>: once a book (and later a chapter) is seen,
/// it persists for <see cref="UtteranceWindow"/> subsequent utterances even across
/// non-reference speech, so a later bare "fifth chapter" or "verse twelve" attaches to it.
/// Any new detection refreshes the window. Ambiguity ("Psalm one nineteen" — 119 vs 1:19)
/// is resolved to the most likely reading with reduced <see cref="BibleReference.Confidence"/>
/// so the confirm gate can intervene.
/// </summary>
public sealed class ReferenceParser
{
    private const int MaxVerse = 200; // Psalm 119:176 is the longest chapter; anything above is noise.
    private const double CueBoost = 1.15; // confidence multiplier when a lead-in cue precedes a book

    /// <summary>Default number of utterances the sticky context survives without a refresh.</summary>
    public const int DefaultUtteranceWindow = 15;

    private BookInfo? _contextBook;
    private int _contextChapter;
    private int _windowRemaining;

    // The last fully-emitted reference. Unlike the sticky context above, this does NOT
    // decay with the window — it persists so an explicit "continue to verse 13" long after
    // the speaker moved on still resumes the passage. Only an explicit verse/chapter cue
    // (never a bare number) may resume it, so a stray later number cannot hijack it.
    private BookInfo? _lastBook;
    private int _lastChapter;

    /// <summary>
    /// How many utterances a detected book/chapter stays "in context" before it decays.
    /// A speaker often names the book, then the chapter, then the verse in separate breaths
    /// with filler between; the window bridges that. Larger = more forgiving of long gaps
    /// but more prone to attaching a stray later number to a stale book.
    /// </summary>
    public int UtteranceWindow { get; set; } = DefaultUtteranceWindow;

    /// <summary>Clears the sticky context and the persistent last-reference memory.</summary>
    public void Reset()
    {
        _contextBook = null;
        _contextChapter = 0;
        _windowRemaining = 0;
        _lastBook = null;
        _lastChapter = 0;
    }

    /// <summary>
    /// Parses one utterance, carrying book/chapter context forward from previous calls
    /// on the same instance (subject to <see cref="UtteranceWindow"/>). Returns detections
    /// ordered by position in the utterance.
    /// </summary>
    public IReadOnlyList<BibleReference> Parse(string utterance)
    {
        ArgumentNullException.ThrowIfNull(utterance);

        // Age the context by one utterance; it decays after the window elapses. The
        // persistent last-reference memory (_lastBook/_lastChapter) deliberately survives this.
        if (_contextBook is not null && --_windowRemaining <= 0)
        {
            _contextBook = null;
            _contextChapter = 0;
            _windowRemaining = 0;
        }

        var tokens = Tokenizer.Tokenize(utterance);

        // Post-decay resumption: no live context, but an explicit verse/chapter cue paired
        // with a bare number ("let's continue to verse 13") resumes the last passage even
        // long after the window lapsed. Requires the explicit keyword — a bare number alone
        // is too ambiguous to hijack a passage the speaker may have left.
        if (_contextBook is null && _lastBook is not null && HasExplicitVerseOrChapterCue(tokens))
        {
            _contextBook = _lastBook;
            _contextChapter = _lastChapter;
            _windowRemaining = Math.Max(1, UtteranceWindow);
        }
        var results = new List<BibleReference>();
        var i = 0;
        while (i < tokens.Count)
        {
            if (BookCatalog.TryMatch(tokens, i, out var match))
            {
                var hasCue = HasCueBefore(tokens, i);
                var bookFactor = hasCue ? Math.Min(1.0, match.Factor * CueBoost) : match.Factor;

                if (TryReadBody(tokens, SkipBookSuffix(tokens, match.NextIndex), match.Book, bookFactor, results, out var consumed))
                {
                    i = consumed;
                    continue;
                }

                // Book named without following numbers. Only trust it as a standing context
                // when it is unambiguous — an exact, non-fuzzy match — or a cue precedes it
                // ("turn to Numbers"). This stops everyday words ("the numbers don't add up",
                // a stray fuzzy hit) from arming a phantom book context.
                if (hasCue || (match.Factor >= 1.0 && !BookCatalog.IsAmbiguousWord(match.Book, tokens[i])))
                {
                    SetContext(match.Book, chapter: 0);
                }
                i = match.NextIndex;
                continue;
            }
            if (TryReadReversedPsalm(tokens, i, results, out var afterPsalm))
            {
                i = afterPsalm;
                continue;
            }
            if (TryReadContinuation(tokens, i, atUtteranceStart: i == 0, results, out var afterContinuation))
            {
                i = afterContinuation;
                continue;
            }
            i++;
        }
        return results;
    }

    /// <summary>Reads chapter/verse following a matched book name.</summary>
    private bool TryReadBody(
        List<Token> tokens, int pos, BookInfo book, double bookFactor,
        List<BibleReference> results, out int consumed)
    {
        consumed = pos;
        int chapter;
        int? verseStart = null;
        var explicitChapter = false;
        double structureFactor;

        if (TryReadChapterToken(tokens, pos, out var kwChapter, out var afterChapter))
        {
            chapter = kwChapter;
            explicitChapter = true;
            pos = afterChapter;
        }
        else if (NumberAt(tokens, pos, out var bareChapter))
        {
            chapter = bareChapter;
            pos++;
        }
        else if (book.ChapterCount == 1 && TryReadVerseToken(tokens, pos, out var soloVerse, out var afterVerse))
        {
            // "Jude verse three"
            var end = ReadRange(tokens, afterVerse, soloVerse, ref afterVerse);
            Emit(results, book, 1, soloVerse, end, bookFactor * 0.95);
            consumed = afterVerse;
            return true;
        }
        else
        {
            return false;
        }

        if (book.ChapterCount == 1 && !explicitChapter)
        {
            // Single-chapter books: "Jude three" means verse 3.
            if (chapter > MaxVerse)
            {
                return false;
            }
            var end = ReadRange(tokens, pos, chapter, ref pos);
            Emit(results, book, 1, chapter, end, bookFactor * 0.95);
            consumed = pos;
            return true;
        }

        if (chapter < 1 || chapter > book.ChapterCount)
        {
            return false;
        }

        // "Psalm one nineteen": spoken 119, not 1:19 — prefer the chapter reading, flag it.
        if (book == BookCatalog.Psalms && chapter == 1 && !explicitChapter
            && NumberAt(tokens, pos, out var combined) && combined is >= 10 and <= 50)
        {
            chapter = 100 + combined;
            pos++;
            structureFactor = 0.6;
            if (TryReadVerseToken(tokens, pos, out var psalmVerse, out var afterPsalmVerse) && psalmVerse <= MaxVerse)
            {
                verseStart = psalmVerse;
                pos = afterPsalmVerse;
                structureFactor = 0.75;
            }
        }
        else if (tokens.Count > pos && tokens[pos].Kind == TokenKind.Colon && NumberAt(tokens, pos + 1, out var colonVerse))
        {
            verseStart = colonVerse;
            pos += 2;
            structureFactor = 1.0;
        }
        else if (TryReadVerseToken(tokens, SkipVerseConnective(tokens, pos), out var kwVerse, out var afterKwVerse))
        {
            verseStart = kwVerse;
            pos = afterKwVerse;
            structureFactor = 1.0;
        }
        else if (NumberAt(tokens, pos, out var bareVerse) && bareVerse <= MaxVerse)
        {
            verseStart = bareVerse;
            pos++;
            structureFactor = 0.95;
        }
        else
        {
            structureFactor = explicitChapter ? 1.0 : 0.95; // chapter-only reference
        }

        int? verseEnd = verseStart is { } vs ? ReadRange(tokens, pos, vs, ref pos) : null;
        Emit(results, book, chapter, verseStart, verseEnd, bookFactor * structureFactor);
        consumed = pos;
        return true;
    }

    /// <summary>"the twenty-third psalm" — number before the (singular) book name.</summary>
    private bool TryReadReversedPsalm(List<Token> tokens, int i, List<BibleReference> results, out int consumed)
    {
        consumed = i;
        // Guard against "fifth chapter" etc. — only fire when the word after the number is "psalm".
        if (!NumberAt(tokens, i, out var chapter) || chapter < 1 || chapter > BookCatalog.Psalms.ChapterCount
            || !IsWord(tokens, i + 1, "psalm"))
        {
            return false;
        }
        var pos = i + 2;
        int? verseStart = null;
        int? verseEnd = null;
        if (TryReadVerseToken(tokens, pos, out var verse, out var afterVerse) && verse <= MaxVerse)
        {
            verseStart = verse;
            pos = afterVerse;
            verseEnd = ReadRange(tokens, pos, verse, ref pos);
        }
        Emit(results, BookCatalog.Psalms, chapter, verseStart, verseEnd, 0.95);
        consumed = pos;
        return true;
    }

    /// <summary>
    /// Continues the sticky context with a bare chapter or verse, in either word order:
    /// "chapter four", "the fifth chapter", "verse seventeen", "verses 13 to 18", or a bare
    /// number when a chapter is already established. Fires anywhere in the utterance, so
    /// filler around the reference ("let's go to the fifth chapter") is tolerated.
    /// </summary>
    private bool TryReadContinuation(
        List<Token> tokens, int i, bool atUtteranceStart, List<BibleReference> results, out int consumed)
    {
        consumed = i;
        if (_contextBook is null)
        {
            return false;
        }

        // Chapter mention → set/replace the context chapter (verse may follow in the same utterance).
        if (TryReadChapterToken(tokens, i, out var chapter, out var afterChapter)
            && chapter >= 1 && chapter <= _contextBook.ChapterCount)
        {
            var pos = afterChapter;
            int? verseStart = null;
            int? verseEnd = null;
            if (TryReadVerseToken(tokens, SkipVerseConnective(tokens, pos), out var verse, out var afterVerse) && verse <= MaxVerse)
            {
                verseStart = verse;
                pos = afterVerse;
                verseEnd = ReadRange(tokens, pos, verse, ref pos);
            }
            Emit(results, _contextBook, chapter, verseStart, verseEnd, 0.9);
            consumed = pos;
            return true;
        }

        // Verse mention against an established chapter: "verse twelve", "verses 13 to 18".
        // Single-chapter books have an implicit chapter 1, so "Jude" then "verse three" works.
        var effectiveChapter = _contextChapter > 0 ? _contextChapter
            : _contextBook.ChapterCount == 1 ? 1 : 0;
        if (effectiveChapter > 0 && TryReadVerseToken(tokens, i, out var soloVerse, out var afterSoloVerse)
            && soloVerse <= MaxVerse)
        {
            var pos = afterSoloVerse;
            var verseEnd = ReadRange(tokens, pos, soloVerse, ref pos);
            Emit(results, _contextBook, effectiveChapter, soloVerse, verseEnd, 0.9);
            consumed = pos;
            return true;
        }

        // Verse cue without the word "verse": "start from one", "beginning at three", "pick up
        // at twelve". The cue is the disambiguating signal, so a bare number attaches as a verse
        // even mid-utterance and even with a trailing tail ("we'll start from one" | ", but we can").
        if (effectiveChapter > 0 && NumberAt(tokens, i, out var cuedVerse) && cuedVerse <= MaxVerse
            && HasVerseCueBefore(tokens, i))
        {
            var pos = i + 1;
            var verseEnd = ReadRange(tokens, pos, cuedVerse, ref pos);
            Emit(results, _contextBook, effectiveChapter, cuedVerse, verseEnd, 0.8);
            consumed = pos;
            return true;
        }

        // A bare leading number continues a split reference, but only at the very start of an
        // utterance AND only when the utterance is a clean reference fragment — mid-sentence
        // bare numbers and prose like "four people came forward" must not attach to context.
        if (atUtteranceStart && NumberAt(tokens, i, out var number))
        {
            if (_contextChapter == 0)
            {
                // Book named last utterance, chapter arrives now: "First Corinthians" | "thirteen verse four".
                if (_contextBook.ChapterCount == 1)
                {
                    if (number > MaxVerse)
                    {
                        return false;
                    }
                    var vpos = i + 1;
                    var vend = ReadRange(tokens, vpos, number, ref vpos);
                    if (!IsCleanTail(tokens, vpos))
                    {
                        return false;
                    }
                    Emit(results, _contextBook, 1, number, vend, 0.7);
                    consumed = vpos;
                    return true;
                }
                if (number < 1 || number > _contextBook.ChapterCount)
                {
                    return false;
                }
                var pos = i + 1;
                int? verseStart = null;
                int? verseEnd = null;
                if (TryReadVerseToken(tokens, pos, out var verse, out var afterVerse) && verse <= MaxVerse)
                {
                    verseStart = verse;
                    pos = afterVerse;
                    verseEnd = ReadRange(tokens, pos, verse, ref pos);
                }
                else if (NumberAt(tokens, pos, out var bareVerse) && bareVerse <= MaxVerse)
                {
                    verseStart = bareVerse;
                    pos++;
                    verseEnd = ReadRange(tokens, pos, bareVerse, ref pos);
                }
                if (!IsCleanTail(tokens, pos))
                {
                    return false;
                }
                Emit(results, _contextBook, number, verseStart, verseEnd, 0.8);
                consumed = pos;
                return true;
            }

            if (number <= MaxVerse)
            {
                // Chapter already set, bare number is the verse: "John three" | "sixteen through eighteen".
                var pos = i + 1;
                var verseEnd = ReadRange(tokens, pos, number, ref pos);
                if (!IsCleanTail(tokens, pos))
                {
                    return false;
                }
                Emit(results, _contextBook, _contextChapter, number, verseEnd, 0.75);
                consumed = pos;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when position <paramref name="pos"/> is the end of the utterance or another
    /// reference token. A bare-number continuation is only trusted when its tail is clean;
    /// if an unrelated word follows ("four people came forward"), it is prose, not a verse.
    /// </summary>
    private static bool IsCleanTail(List<Token> tokens, int pos)
    {
        if (pos >= tokens.Count)
        {
            return true;
        }
        var token = tokens[pos];
        return token.Kind is TokenKind.Number or TokenKind.Colon or TokenKind.Dash
            || IsWord(tokens, pos, "verse", "verses", "chapter", "through", "to", "thru", "and");
    }

    /// <summary>
    /// True if a book-cue phrase ("turn to", "the book of", …) ends immediately before the book
    /// at <paramref name="bookIndex"/>. Boosts confidence and rescues shaky matches.
    /// </summary>
    private static bool HasCueBefore(List<Token> tokens, int bookIndex) =>
        HasPhraseEndingAt(tokens, bookIndex, ReferenceCues.Phrases);

    /// <summary>
    /// True if a verse-cue phrase ("start from", "beginning at", …) ends immediately before the
    /// number at <paramref name="index"/> — the signal that a bare number is a verse.
    /// </summary>
    private static bool HasVerseCueBefore(List<Token> tokens, int index) =>
        HasPhraseEndingAt(tokens, index, ReferenceCues.VersePhrases);

    /// <summary>True if any phrase in <paramref name="phrases"/> occupies the words ending just before <paramref name="index"/>.</summary>
    private static bool HasPhraseEndingAt(List<Token> tokens, int index, IReadOnlyList<string[]> phrases)
    {
        if (index == 0)
        {
            return false;
        }
        foreach (var phrase in phrases)
        {
            var start = index - phrase.Length;
            if (start < 0)
            {
                continue;
            }
            var matched = true;
            for (var k = 0; k < phrase.Length; k++)
            {
                if (tokens[start + k].Kind != TokenKind.Word || tokens[start + k].Word != phrase[k])
                {
                    matched = false;
                    break;
                }
            }
            if (matched)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True if the utterance contains an explicit "verse N"/"N verse" or "chapter N"/"N chapter"
    /// anywhere. Gate for post-decay resumption: only a deliberate cue may resume a lapsed
    /// passage, so an incidental bare number cannot.
    /// </summary>
    private static bool HasExplicitVerseOrChapterCue(List<Token> tokens)
    {
        for (var p = 0; p < tokens.Count; p++)
        {
            if (TryReadVerseToken(tokens, p, out _, out _) || TryReadChapterToken(tokens, p, out _, out _))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Matches "chapter N" or "N chapter" (spoken "the fifth chapter"). Advances past both tokens.
    /// The forward form tolerates an interposed filler ("chapter number 20"); the reversed form
    /// does not — "number 20 chapter" is not real speech, and loosening it invites false positives.
    /// </summary>
    private static bool TryReadChapterToken(List<Token> tokens, int pos, out int chapter, out int next)
    {
        if (IsWord(tokens, pos, "chapter"))
        {
            var numberPos = SkipKeywordFiller(tokens, pos + 1);
            if (NumberAt(tokens, numberPos, out chapter))
            {
                next = numberPos + 1;
                return true;
            }
        }
        if (NumberAt(tokens, pos, out chapter) && IsWord(tokens, pos + 1, "chapter"))
        {
            next = pos + 2;
            return true;
        }
        chapter = 0;
        next = pos;
        return false;
    }

    /// <summary>
    /// Matches "verse N" or "N verse". Advances past both tokens. As with chapters, only the
    /// forward form tolerates an interposed filler ("verse number 27").
    /// </summary>
    private static bool TryReadVerseToken(List<Token> tokens, int pos, out int verse, out int next)
    {
        if (IsVerseWord(tokens, pos))
        {
            var numberPos = SkipKeywordFiller(tokens, pos + 1);
            if (NumberAt(tokens, numberPos, out verse))
            {
                next = numberPos + 1;
                return true;
            }
        }
        if (NumberAt(tokens, pos, out verse) && IsVerseWord(tokens, pos + 1))
        {
            next = pos + 2;
            return true;
        }
        verse = 0;
        next = pos;
        return false;
    }

    /// <summary>"sixteen through eighteen", "16-18", "13 to 18" — returns the range end, advancing pos.</summary>
    private static int? ReadRange(List<Token> tokens, int pos, int verseStart, ref int consumed)
    {
        var isRangeMarker = (tokens.Count > pos && tokens[pos].Kind == TokenKind.Dash)
            || IsWord(tokens, pos, "through", "to", "thru");
        if (isRangeMarker && NumberAt(tokens, pos + 1, out var end) && end > verseStart && end <= MaxVerse)
        {
            consumed = pos + 2;
            return end;
        }
        consumed = Math.Max(consumed, pos);
        return null;
    }

    private void Emit(
        List<BibleReference> results, BookInfo book, int chapter,
        int? verseStart, int? verseEnd, double confidence)
    {
        results.Add(new BibleReference(book.Name, chapter, verseStart, verseEnd, Math.Clamp(confidence, 0, 1)));
        SetContext(book, chapter);
        // Persist the last complete reference (book + real chapter) for post-decay resumption.
        if (chapter > 0)
        {
            _lastBook = book;
            _lastChapter = chapter;
        }
    }

    /// <summary>Sets the sticky context and refreshes its decay window.</summary>
    private void SetContext(BookInfo book, int chapter)
    {
        _contextBook = book;
        _contextChapter = chapter;
        _windowRemaining = Math.Max(1, UtteranceWindow);
    }

    /// <summary>
    /// Skips a filler word that speakers insert between a chapter/verse keyword and its number
    /// ("chapter number 20", "verse no. 27"). At most one, and only ever consumed when a number
    /// follows — the callers re-check that, so nothing is swallowed from ordinary prose.
    /// </summary>
    private static int SkipKeywordFiller(List<Token> tokens, int pos) =>
        IsWord(tokens, pos, "number", "numbers", "no") ? pos + 1 : pos;

    /// <summary>
    /// Skips connectives a speaker uses to link a chapter to its verse ("chapter 20, from verse
    /// 27", "chapter 3 and verse 16"). Lookahead-and-commit: the skip only happens when a verse
    /// keyword genuinely follows, so "chapter 20 from the pulpit" is untouched.
    /// </summary>
    private static int SkipVerseConnective(List<Token> tokens, int pos)
    {
        if (IsWord(tokens, pos, "from", "at", "in", "and", "starting", "beginning")
            && IsVerseWord(tokens, pos + 1))
        {
            return pos + 1;
        }
        return pos;
    }

    /// <summary>
    /// Skips an appositive that follows a spoken book name ("Saint Matthew's gospel, chapter 25").
    /// Lookahead-and-commit like <see cref="SkipVerseConnective"/>: only skipped when chapter/verse
    /// grammar genuinely follows, so prose ("Mark's gospel was written…") is untouched.
    /// Only the gospel form needs this — letters put the book name last ("Paul's letter to the
    /// Romans chapter 8"), which already parses.
    /// </summary>
    private static int SkipBookSuffix(List<Token> tokens, int pos) =>
        IsWord(tokens, pos, "gospel", "gospels")
        && (IsWord(tokens, pos + 1, "chapter") || IsVerseWord(tokens, pos + 1) || NumberAt(tokens, pos + 1, out _))
            ? pos + 1
            : pos;

    private static bool IsWord(List<Token> tokens, int index, params string[] words) =>
        index < tokens.Count && tokens[index].Kind == TokenKind.Word && words.Contains(tokens[index].Word);

    private static bool IsVerseWord(List<Token> tokens, int index) => IsWord(tokens, index, "verse", "verses");

    private static bool NumberAt(List<Token> tokens, int index, out int value)
    {
        value = 0;
        if (index < tokens.Count && tokens[index].Kind == TokenKind.Number)
        {
            value = tokens[index].Value;
            return true;
        }
        return false;
    }
}
