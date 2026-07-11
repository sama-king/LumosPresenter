using LumosPresenter.Core.Domain;
using LumosPresenter.Core.Parsing;

namespace LumosPresenter.Tests.Parsing;

public class ReferenceParserTests
{
    private static BibleReference ParseSingle(string utterance)
    {
        var refs = new ReferenceParser().Parse(utterance);
        Assert.Single(refs);
        return refs[0];
    }

    [Theory]
    // Bare book + spoken numbers
    [InlineData("john three sixteen", "John 3:16")]
    [InlineData("genesis one one", "Genesis 1:1")]
    [InlineData("isaiah fifty three five", "Isaiah 53:5")]
    [InlineData("matthew twenty eight nineteen", "Matthew 28:19")]
    [InlineData("acts two thirty eight", "Acts 2:38")]
    [InlineData("revelation three twenty", "Revelation 3:20")]
    [InlineData("hebrews eleven one", "Hebrews 11:1")]
    [InlineData("james one five", "James 1:5")]
    [InlineData("deuteronomy six four", "Deuteronomy 6:4")]
    [InlineData("ecclesiastes three one", "Ecclesiastes 3:1")]
    [InlineData("malachi three ten", "Malachi 3:10")]
    // Digit forms
    [InlineData("john 3 16", "John 3:16")]
    [InlineData("john 3:16", "John 3:16")]
    [InlineData("psalm 23:1", "Psalms 23:1")]
    [InlineData("2 timothy 3:16", "2 Timothy 3:16")]
    // Explicit chapter/verse keywords
    [InlineData("john chapter three verse sixteen", "John 3:16")]
    [InlineData("turn with me to romans chapter eight verse twenty eight", "Romans 8:28")]
    [InlineData("luke chapter fifteen", "Luke 15")]
    // Chapter-only
    [InlineData("john three", "John 3")]
    [InlineData("numbers twenty two", "Numbers 22")]
    [InlineData("ezekiel thirty seven", "Ezekiel 37")]
    // Ordinal-prefixed books
    [InlineData("first corinthians thirteen", "1 Corinthians 13")]
    [InlineData("first corinthians chapter thirteen verse four", "1 Corinthians 13:4")]
    [InlineData("second timothy two fifteen", "2 Timothy 2:15")]
    [InlineData("1st john four eight", "1 John 4:8")]
    [InlineData("i corinthians one eighteen", "1 Corinthians 1:18")]
    [InlineData("second chronicles seven fourteen", "2 Chronicles 7:14")]
    [InlineData("1 kings eighteen twenty one", "1 Kings 18:21")]
    [InlineData("first peter five seven", "1 Peter 5:7")]
    [InlineData("one john one nine", "1 John 1:9")]
    // Single-chapter books: bare number means verse
    [InlineData("jude three", "Jude 1:3")]
    [InlineData("jude verse three", "Jude 1:3")]
    [InlineData("philemon six", "Philemon 1:6")]
    [InlineData("third john verse four", "3 John 1:4")]
    // Common mis-transcriptions
    [InlineData("filipians four thirteen", "Philippians 4:13")]
    [InlineData("galations five twenty two", "Galatians 5:22")]
    [InlineData("revelations three twenty", "Revelation 3:20")]
    [InlineData("habakuk two four", "Habakkuk 2:4")]
    [InlineData("mathew five three", "Matthew 5:3")]
    // Multi-word book names
    [InlineData("song of solomon two four", "Song of Solomon 2:4")]
    [InlineData("song of songs two four", "Song of Solomon 2:4")]
    // Large spoken numbers
    [InlineData("psalm one hundred and nineteen verse eleven", "Psalms 119:11")]
    [InlineData("psalm a hundred and fifty", "Psalms 150")]
    [InlineData("psalm one oh three", "Psalms 103")]
    // Reversed psalm form
    [InlineData("the twenty third psalm", "Psalms 23")]
    [InlineData("the twenty-third psalm", "Psalms 23")]
    [InlineData("the third psalm", "Psalms 3")]
    // Ranges
    [InlineData("john three sixteen through eighteen", "John 3:16-18")]
    [InlineData("romans 8:28-30", "Romans 8:28-30")]
    [InlineData("proverbs three five through six", "Proverbs 3:5-6")]
    [InlineData("first thessalonians four verses thirteen to eighteen", "1 Thessalonians 4:13-18")]
    [InlineData("matthew five three through twelve", "Matthew 5:3-12")]
    // Explicit verse keyword beats the Psalm 119 heuristic
    [InlineData("psalm one verse nineteen", "Psalms 1:19")]
    // "Nth chapter" / "Nth verse" — number before the keyword (spoken ordinal form)
    [InlineData("john the third chapter", "John 3")]
    [InlineData("romans chapter eight verse twenty eight", "Romans 8:28")]
    [InlineData("genesis first chapter first verse", "Genesis 1:1")]
    [InlineData("john third chapter sixteenth verse", "John 3:16")]
    public void Parse_DetectsReference(string utterance, string expected)
    {
        Assert.Equal(expected, ParseSingle(utterance).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("good morning everyone, please be seated")]
    [InlineData("let us pray")]
    [InlineData("i walked three miles yesterday")]
    [InlineData("she has a job to do")]
    [InlineData("acts of kindness matter")]
    [InlineData("please open your bibles to the book of john")] // book named, no reference
    [InlineData("john twenty five three")] // John has 21 chapters — out of range
    [InlineData("verse sixteen")] // no context on a fresh parser
    // "numbers" the everyday word must not become the book Numbers without a cue or numbers
    [InlineData("the numbers don't add up")]
    [InlineData("point number two is important")]       // "number" singular — no fuzzy to Numbers
    [InlineData("we were numbered among them")]         // "numbered" — no fuzzy to Numbers
    [InlineData("look at all these numbers")]           // ambiguous word, no cue, no chapter/verse
    public void Parse_NonReference_ReturnsEmpty(string utterance)
    {
        Assert.Empty(new ReferenceParser().Parse(utterance));
    }

    [Fact]
    public void Parse_NullUtterance_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new ReferenceParser().Parse(null!));
    }

    [Fact]
    public void Parse_MultipleReferencesInOneUtterance()
    {
        var refs = new ReferenceParser().Parse("john three sixteen and romans five eight");
        Assert.Equal(["John 3:16", "Romans 5:8"], refs.Select(r => r.ToString()));
    }

    [Fact]
    public void Parse_ContextCarriesAcrossUtterances()
    {
        var parser = new ReferenceParser();
        Assert.Equal("John 3", Assert.Single(parser.Parse("turn to john chapter three")).ToString());
        Assert.Equal("John 3:16", Assert.Single(parser.Parse("verse sixteen")).ToString());
        Assert.Equal("John 3:17", Assert.Single(parser.Parse("and verse seventeen")).ToString());
        Assert.Equal("John 5:1", Assert.Single(parser.Parse("chapter five verse one")).ToString());
    }

    [Fact]
    public void Reset_ClearsContinuationContext()
    {
        var parser = new ReferenceParser();
        parser.Parse("john chapter three");
        parser.Reset();
        Assert.Empty(parser.Parse("verse sixteen"));
    }

    [Fact]
    public void Parse_ExactReference_HasHighConfidence()
    {
        Assert.True(ParseSingle("john chapter three verse sixteen").Confidence >= 0.95);
        Assert.True(ParseSingle("john 3:16").Confidence >= 0.95);
    }

    [Fact]
    public void Parse_AmbiguousPsalm_PrefersChapterReadingWithLowConfidence()
    {
        var reference = ParseSingle("psalm one nineteen");
        Assert.Equal("Psalms 119", reference.ToString());
        Assert.True(reference.Confidence <= 0.65);
    }

    [Fact]
    public void Parse_FuzzyBookName_ReducesConfidence()
    {
        var reference = ParseSingle("colossans three sixteen"); // not in the curated alias list
        Assert.Equal("Colossians 3:16", reference.ToString());
        Assert.InRange(reference.Confidence, 0.6, 0.9);
    }

    [Fact]
    public void Parse_BareNumberedBase_ResolvesToFirstBookWithLowConfidence()
    {
        var reference = ParseSingle("corinthians thirteen four");
        Assert.Equal("1 Corinthians 13:4", reference.ToString());
        Assert.True(reference.Confidence < 0.75);
    }

    [Fact]
    public void Parse_ContinuationReference_HasReducedConfidence()
    {
        var parser = new ReferenceParser();
        parser.Parse("john chapter three");
        var reference = Assert.Single(parser.Parse("verse sixteen"));
        Assert.InRange(reference.Confidence, 0.8, 0.95);
    }

    [Fact]
    public void Parse_SplitReferenceWithVerseUpgrade()
    {
        // "turn to john chapter three… verse sixteen" split across ASR utterances
        var parser = new ReferenceParser();
        var first = Assert.Single(parser.Parse("turn to john chapter three"));
        Assert.Equal("John 3", first.ToString());
        Assert.Null(first.VerseStart);
        var second = Assert.Single(parser.Parse("verse sixteen"));
        Assert.Equal(16, second.VerseStart);
    }

    // --- References split across utterances by ASR endpointing (pauses) ---

    [Fact]
    public void SplitByPause_DanglingVerseKeyword()
    {
        var parser = new ReferenceParser();
        Assert.Equal("John 3", Assert.Single(parser.Parse("john chapter three verse")).ToString());
        Assert.Equal("John 3:16", Assert.Single(parser.Parse("sixteen")).ToString());
    }

    [Fact]
    public void SplitByPause_BookOnlyThenNumbers()
    {
        var parser = new ReferenceParser();
        Assert.Empty(parser.Parse("please turn with me to first corinthians"));
        Assert.Equal("1 Corinthians 13:4", Assert.Single(parser.Parse("thirteen verse four")).ToString());
    }

    [Fact]
    public void SplitByPause_BookOnlyThenChapterKeyword()
    {
        var parser = new ReferenceParser();
        Assert.Empty(parser.Parse("turn to john"));
        Assert.Equal("John 3:16", Assert.Single(parser.Parse("chapter three verse sixteen")).ToString());
    }

    [Fact]
    public void SplitByPause_DanglingChapterKeyword()
    {
        var parser = new ReferenceParser();
        Assert.Empty(parser.Parse("john chapter"));
        Assert.Equal("John 3:16", Assert.Single(parser.Parse("three sixteen")).ToString());
    }

    [Fact]
    public void SplitByPause_BareVerseAfterChapterOnly()
    {
        var parser = new ReferenceParser();
        Assert.Equal("John 3", Assert.Single(parser.Parse("john three")).ToString());
        var upgraded = Assert.Single(parser.Parse("sixteen through eighteen"));
        Assert.Equal("John 3:16-18", upgraded.ToString());
        Assert.True(upgraded.Confidence < 0.85); // bare-number continuation is a guess — confirm gate decides
    }

    [Fact]
    public void SplitByPause_SingleChapterBookThenVerse()
    {
        var parser = new ReferenceParser();
        Assert.Empty(parser.Parse("turn to jude"));
        Assert.Equal("Jude 1:3", Assert.Single(parser.Parse("verse three")).ToString());
    }

    [Fact]
    public void StickyContext_SurvivesFillerUtterances()
    {
        // The whole point of the window: a verse arriving after filler still resolves.
        var parser = new ReferenceParser();
        Assert.Equal("John 3", Assert.Single(parser.Parse("john chapter three verse")).ToString());
        Assert.Empty(parser.Parse("let us pray together"));
        Assert.Equal("John 3:16", Assert.Single(parser.Parse("sixteen")).ToString());
    }

    [Fact]
    public void StickyContext_ResolvesBookChapterVerseAcrossThreeUtterancesWithFiller()
    {
        // The real-world failure that motivated the window: "Ephesians" … filler …
        // "the fifth chapter" … "verse twelve".
        var parser = new ReferenceParser();
        Assert.Empty(parser.Parse("so let's turn our bibles to the book of"));
        Assert.Empty(parser.Parse("ephesians"));
        Assert.Empty(parser.Parse("we are going to be looking at the word"));
        Assert.Equal("Ephesians 5", Assert.Single(parser.Parse("let's go to the fifth chapter")).ToString());
        Assert.Equal("Ephesians 5:12", Assert.Single(parser.Parse("verse twelve")).ToString());
    }

    [Fact]
    public void StickyContext_DecaysAfterUtteranceWindow()
    {
        var parser = new ReferenceParser { UtteranceWindow = 2 };
        parser.Parse("turn to first corinthians");
        parser.Parse("let us consider love");
        parser.Parse("as we think about this today");
        Assert.Empty(parser.Parse("thirteen verse four")); // context expired past the window
    }

    [Fact]
    public void StickyContext_WithinWindow_StillResolves()
    {
        var parser = new ReferenceParser { UtteranceWindow = 3 };
        parser.Parse("turn to first corinthians");
        parser.Parse("let us consider love");
        Assert.Equal("1 Corinthians 13:4", Assert.Single(parser.Parse("thirteen verse four")).ToString());
    }

    // --- Persistent last-reference: an explicit verse cue resumes the passage even after
    //     the sticky window has fully decayed ("Ephesians 5:12" … much later … "verse 13"). ---

    [Fact]
    public void LastReference_ProgressesAfterWindowDecay()
    {
        var parser = new ReferenceParser { UtteranceWindow = 3 };
        Assert.Equal("Ephesians 5:12", Assert.Single(parser.Parse("ephesians five verse twelve")).ToString());
        for (var i = 0; i < 6; i++)
        {
            Assert.Empty(parser.Parse("and the lord spoke to his people that day"));
        }
        // Window long gone, but an explicit verse cue resumes the last passage.
        Assert.Equal("Ephesians 5:13", Assert.Single(parser.Parse("let's continue to verse thirteen")).ToString());
    }

    [Fact]
    public void LastReference_PlainVerseKeywordResumesAfterDecay()
    {
        var parser = new ReferenceParser { UtteranceWindow = 2 };
        parser.Parse("ephesians five verse twelve");
        parser.Parse("filler one");
        parser.Parse("filler two");
        Assert.Equal("Ephesians 5:13", Assert.Single(parser.Parse("verse thirteen")).ToString());
    }

    [Fact]
    public void LastReference_ChapterCueResumesBookAfterDecay()
    {
        var parser = new ReferenceParser { UtteranceWindow = 2 };
        parser.Parse("ephesians five verse twelve");
        parser.Parse("filler one");
        parser.Parse("filler two");
        Assert.Equal("Ephesians 6", Assert.Single(parser.Parse("let's move to chapter six")).ToString());
    }

    [Fact]
    public void LastReference_RangeContinuationResumesAfterDecay()
    {
        var parser = new ReferenceParser { UtteranceWindow = 2 };
        parser.Parse("ephesians five verse twelve");
        parser.Parse("filler one");
        parser.Parse("filler two");
        Assert.Equal("Ephesians 5:13-15",
            Assert.Single(parser.Parse("continue with verses thirteen through fifteen")).ToString());
    }

    [Theory]
    [InlineData("thirteen people were there that morning")] // bare number in prose
    [InlineData("thirteen")]                                 // naked number alone
    [InlineData("and then we sang a hymn together")]         // no number at all
    public void LastReference_BareNumberOrProse_DoesNotResumeAfterDecay(string utterance)
    {
        // Only an explicit verse/chapter cue may resume a lapsed passage; a stray number must not.
        var parser = new ReferenceParser { UtteranceWindow = 2 };
        parser.Parse("ephesians five verse twelve");
        parser.Parse("filler one");
        parser.Parse("filler two");
        Assert.Empty(parser.Parse(utterance));
    }

    [Fact]
    public void LastReference_ClearedByReset()
    {
        var parser = new ReferenceParser { UtteranceWindow = 2 };
        parser.Parse("ephesians five verse twelve");
        parser.Reset();
        Assert.Empty(parser.Parse("continue to verse thirteen"));
    }

    [Fact]
    public void StickyContext_BareNumberProse_DoesNotAttach()
    {
        // "john three" then a sentence starting with a number must not become John 3:4.
        var parser = new ReferenceParser();
        Assert.Equal("John 3", Assert.Single(parser.Parse("john three")).ToString());
        Assert.Empty(parser.Parse("four people came forward that day"));
    }

    [Theory]
    [InlineData("let's go to the fifth chapter", "John 5")]
    [InlineData("look at the third chapter", "John 3")]
    [InlineData("chapter five", "John 5")]
    public void Continuation_ChapterInEitherWordOrder(string utterance, string expected)
    {
        var parser = new ReferenceParser();
        parser.Parse("turn to the book of john");
        Assert.Equal(expected, Assert.Single(parser.Parse(utterance)).ToString());
    }

    [Fact]
    public void Parse_PsalmAmbiguousChapterWithVerse()
    {
        var reference = ParseSingle("psalm one nineteen verse one oh five");
        Assert.Equal("Psalms 119:105", reference.ToString());
        Assert.True(reference.Confidence < 0.9);
    }
}
