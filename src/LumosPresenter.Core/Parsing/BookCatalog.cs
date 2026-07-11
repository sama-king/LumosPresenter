namespace LumosPresenter.Core.Parsing;

/// <summary>Canonical metadata for one book of the 66-book Protestant canon.</summary>
public sealed record BookInfo(int Number, string Name, int ChapterCount);

internal readonly record struct BookMatch(BookInfo Book, double Factor, int NextIndex);

/// <summary>
/// Maps spoken book names — canonical names, abbreviations, and common ASR
/// mis-transcriptions ("filipians" → Philippians) — to canonical books.
/// Ordinal-prefixed books ("First / 1st / I Corinthians") match via a numbered-base
/// table; unknown words fall back to bounded Levenshtein distance with a
/// confidence penalty.
/// </summary>
public static class BookCatalog
{
    public static IReadOnlyList<BookInfo> Books { get; }

    internal static BookInfo Psalms { get; }

    private static readonly Dictionary<string, (BookInfo Book, double Factor)> Aliases = new();
    private static readonly Dictionary<string, BookInfo[]> NumberedBases = new();
    private static readonly HashSet<string> ExactOnlyAliases = new();
    private static readonly string[] SingleWordAliasKeys;
    private static readonly string[] NumberedBaseKeys;

    static BookCatalog()
    {
        var books = new List<BookInfo>(66);

        BookInfo Add(string name, int chapters, params string[] aliases)
        {
            var info = new BookInfo(books.Count + 1, name, chapters);
            books.Add(info);
            foreach (var alias in aliases)
            {
                Aliases[alias] = (info, 1.0);
            }
            return info;
        }

        // A book whose name is also a common English word: it must match EXACTLY, never via
        // fuzzy distance, so "number" / "numbered" don't resolve to the book "Numbers".
        BookInfo AddExact(string name, int chapters, string alias)
        {
            var info = Add(name, chapters, alias);
            ExactOnlyAliases.Add(alias);
            return info;
        }

        // A numbered base ("corinthians") resolves with an ordinal prefix; bare, it
        // falls back to the first book of the group at reduced confidence.
        void Numbered(BookInfo[] group, params string[] baseAliases)
        {
            foreach (var baseAlias in baseAliases)
            {
                NumberedBases[baseAlias] = group;
                Aliases.TryAdd(baseAlias, (group[0], 0.7));
            }
        }

        Add("Genesis", 50, "genesis", "gen");
        Add("Exodus", 40, "exodus", "exod");
        Add("Leviticus", 27, "leviticus", "lev");
        AddExact("Numbers", 36, "numbers");
        Add("Deuteronomy", 34, "deuteronomy", "deut", "dueteronomy");
        Add("Joshua", 24, "joshua", "josh");
        Add("Judges", 21, "judges");
        Add("Ruth", 4, "ruth");
        var sam1 = Add("1 Samuel", 31);
        var sam2 = Add("2 Samuel", 24);
        var kgs1 = Add("1 Kings", 22);
        var kgs2 = Add("2 Kings", 25);
        var chr1 = Add("1 Chronicles", 29);
        var chr2 = Add("2 Chronicles", 36);
        Add("Ezra", 10, "ezra");
        Add("Nehemiah", 13, "nehemiah", "nehemia");
        Add("Esther", 10, "esther");
        Add("Job", 42, "job");
        var psalms = Add("Psalms", 150, "psalms", "psalm");
        Add("Proverbs", 31, "proverbs", "proverb");
        Add("Ecclesiastes", 12, "ecclesiastes", "ecclesiastics");
        Add("Song of Solomon", 8, "song of solomon", "songs of solomon", "song of songs", "canticles");
        Add("Isaiah", 66, "isaiah", "isaia");
        Add("Jeremiah", 52, "jeremiah", "jeremia");
        Add("Lamentations", 5, "lamentations");
        Add("Ezekiel", 48, "ezekiel", "ezekial");
        Add("Daniel", 12, "daniel");
        Add("Hosea", 14, "hosea");
        Add("Joel", 3, "joel");
        Add("Amos", 9, "amos");
        Add("Obadiah", 1, "obadiah", "obadia");
        Add("Jonah", 4, "jonah", "jona");
        Add("Micah", 7, "micah", "mica");
        Add("Nahum", 3, "nahum");
        Add("Habakkuk", 3, "habakkuk", "habakuk", "habbakuk");
        Add("Zephaniah", 3, "zephaniah", "zephania");
        Add("Haggai", 2, "haggai");
        Add("Zechariah", 14, "zechariah", "zachariah", "zecharia");
        Add("Malachi", 4, "malachi");
        Add("Matthew", 28, "matthew", "mathew", "matt");
        Add("Mark", 16, "mark");
        Add("Luke", 24, "luke");
        var john = Add("John", 21, "john");
        Add("Acts", 28, "acts");
        Add("Romans", 16, "romans");
        var cor1 = Add("1 Corinthians", 16);
        var cor2 = Add("2 Corinthians", 13);
        Add("Galatians", 6, "galatians", "galations");
        Add("Ephesians", 6, "ephesians", "efesians", "ephesans");
        Add("Philippians", 4, "philippians", "philipians", "phillipians", "phillippians", "filipians", "filippians");
        Add("Colossians", 4, "colossians", "collosians", "colosians", "collossians");
        var ths1 = Add("1 Thessalonians", 5);
        var ths2 = Add("2 Thessalonians", 3);
        var tim1 = Add("1 Timothy", 6);
        var tim2 = Add("2 Timothy", 4);
        Add("Titus", 3, "titus");
        Add("Philemon", 1, "philemon", "filemon");
        Add("Hebrews", 13, "hebrews");
        Add("James", 5, "james");
        var pet1 = Add("1 Peter", 5);
        var pet2 = Add("2 Peter", 3);
        var jn1 = Add("1 John", 5);
        var jn2 = Add("2 John", 1);
        var jn3 = Add("3 John", 1);
        Add("Jude", 1, "jude");
        Add("Revelation", 22, "revelation", "revelations");

        Numbered([sam1, sam2], "samuel");
        Numbered([kgs1, kgs2], "kings");
        Numbered([chr1, chr2], "chronicles");
        Numbered([cor1, cor2], "corinthians");
        Numbered([ths1, ths2], "thessalonians", "thesalonians");
        Numbered([tim1, tim2], "timothy");
        Numbered([pet1, pet2], "peter");
        Numbered([jn1, jn2, jn3], "john");

        Books = books;
        Psalms = psalms;
        // Exact-only aliases ("numbers") are excluded so they can never be a fuzzy target.
        SingleWordAliasKeys = [.. Aliases.Keys.Where(k => !k.Contains(' ') && !ExactOnlyAliases.Contains(k))];
        NumberedBaseKeys = [.. NumberedBases.Keys];
    }

    /// <summary>
    /// True when the token that matched this book is also a common English word (e.g. the
    /// word "numbers" matching the book Numbers). Such a match, absent a cue or a following
    /// chapter/verse, is probably ordinary speech rather than a reference.
    /// </summary>
    internal static bool IsAmbiguousWord(BookInfo book, Token token) =>
        token.Kind == TokenKind.Word
        && ExactOnlyAliases.Contains(token.Word)
        && Aliases.TryGetValue(token.Word, out var hit)
        && hit.Book == book;

    internal static bool TryMatch(IReadOnlyList<Token> tokens, int index, out BookMatch match)
    {
        match = default;
        var token = tokens[index];

        // Ordinal prefix: "1 / first / I Corinthians" ("first" is already a Number here).
        var ordinal = token switch
        {
            { Kind: TokenKind.Number, Value: >= 1 and <= 3 } => token.Value,
            { Kind: TokenKind.Word, Word: "i" } => 1,
            _ => 0,
        };
        if (ordinal > 0 && index + 1 < tokens.Count && tokens[index + 1].Kind == TokenKind.Word)
        {
            var baseWord = tokens[index + 1].Word;
            if (NumberedBases.TryGetValue(baseWord, out var group) && ordinal <= group.Length)
            {
                match = new BookMatch(group[ordinal - 1], 1.0, index + 2);
                return true;
            }
            if (baseWord.Length >= 5
                && TryClosest(baseWord, NumberedBaseKeys, out var closestBase, out var baseDistance))
            {
                var fuzzyGroup = NumberedBases[closestBase];
                if (ordinal <= fuzzyGroup.Length)
                {
                    match = new BookMatch(fuzzyGroup[ordinal - 1], FuzzyFactor(baseDistance), index + 2);
                    return true;
                }
            }
        }

        if (token.Kind != TokenKind.Word)
        {
            return false;
        }

        // Multi-word aliases ("song of solomon") before single words.
        for (var length = 3; length >= 2; length--)
        {
            if (index + length > tokens.Count
                || Enumerable.Range(index, length).Any(i => tokens[i].Kind != TokenKind.Word))
            {
                continue;
            }
            var phrase = string.Join(' ', tokens.Skip(index).Take(length).Select(t => t.Word));
            if (Aliases.TryGetValue(phrase, out var phraseHit))
            {
                match = new BookMatch(phraseHit.Book, phraseHit.Factor, index + length);
                return true;
            }
        }

        if (Aliases.TryGetValue(token.Word, out var hit))
        {
            match = new BookMatch(hit.Book, hit.Factor, index + 1);
            return true;
        }

        if (token.Word.Length >= 5
            && TryClosest(token.Word, SingleWordAliasKeys, out var closest, out var distance))
        {
            var fuzzyHit = Aliases[closest];
            match = new BookMatch(fuzzyHit.Book, fuzzyHit.Factor * FuzzyFactor(distance), index + 1);
            return true;
        }

        return false;
    }

    private static double FuzzyFactor(int distance) => distance <= 1 ? 0.85 : 0.7;

    private static bool TryClosest(string word, string[] keys, out string closest, out int distance)
    {
        closest = "";
        distance = int.MaxValue;
        var maxDistance = word.Length >= 8 ? 2 : 1;
        foreach (var key in keys)
        {
            if (Math.Abs(key.Length - word.Length) > maxDistance)
            {
                continue;
            }
            var d = Levenshtein(word, key);
            if (d < distance)
            {
                (closest, distance) = (key, d);
            }
        }
        return distance <= maxDistance;
    }

    private static int Levenshtein(string a, string b)
    {
        Span<int> previous = stackalloc int[b.Length + 1];
        Span<int> current = stackalloc int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            var swap = previous;
            previous = current;
            current = swap;
        }
        return previous[b.Length];
    }
}
