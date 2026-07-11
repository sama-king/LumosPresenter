using LumosPresenter.Core.Parsing;

namespace LumosPresenter.Tests.Parsing;

public class ReferenceCueTests
{
    private static Core.Domain.BibleReference ParseSingle(string utterance)
    {
        var refs = new ReferenceParser().Parse(utterance);
        return Assert.Single(refs);
    }

    // --- The "numbers" everyday-word guard ---

    [Theory]
    [InlineData("turn to numbers chapter five", "Numbers 5")]
    [InlineData("numbers three sixteen", "Numbers 3:16")]        // numbers follow → allowed
    [InlineData("the book of numbers chapter one", "Numbers 1")]
    public void Numbers_ResolvesWithCueOrFollowingNumbers(string utterance, string expected)
    {
        Assert.Equal(expected, ParseSingle(utterance).ToString());
    }

    [Fact]
    public void Numbers_BookOnlyWithCue_SetsContextForNextUtterance()
    {
        var parser = new ReferenceParser();
        Assert.Empty(parser.Parse("let us turn to the book of numbers")); // cue present, no numbers yet
        Assert.Equal("Numbers 5", Assert.Single(parser.Parse("chapter five")).ToString());
    }

    [Fact]
    public void Numbers_BookOnlyWithoutCue_DoesNotArmContext()
    {
        var parser = new ReferenceParser();
        Assert.Empty(parser.Parse("we were reading numbers")); // "reading" alone is not a cue phrase
        Assert.Empty(parser.Parse("chapter five")); // context was never armed by an ambiguous bare word
    }

    // --- Cue-driven confidence ---

    [Fact]
    public void Cue_BoostsConfidenceOfFuzzyMatch()
    {
        // "colossans" is a genuine fuzzy match (not in the curated alias list).
        var without = ParseSingle("colossans three sixteen").Confidence;
        var with = ParseSingle("turn to colossans three sixteen").Confidence;
        Assert.True(with > without, $"expected cue to raise confidence ({with} > {without})");
    }

    [Fact]
    public void Cue_BoostsConfidenceOfAmbiguousNumberedBase()
    {
        var without = ParseSingle("corinthians thirteen four").Confidence;
        var with = ParseSingle("open your bibles to corinthians thirteen four").Confidence;
        Assert.True(with > without, $"expected cue to raise confidence ({with} > {without})");
    }

    [Theory]
    [InlineData("turn to john three sixteen")]
    [InlineData("let's go to romans eight twenty eight")]
    [InlineData("flip over to psalm twenty three")]
    [InlineData("reading from first corinthians thirteen four")]
    public void Cue_DoesNotBreakStrongMatches(string utterance)
    {
        Assert.True(ParseSingle(utterance).Confidence >= 0.9);
    }

    [Fact]
    public void Cues_LibraryIsNonEmptyAndPunctuationFree()
    {
        Assert.NotEmpty(ReferenceCues.Phrases);
        Assert.All(ReferenceCues.Phrases, cue => Assert.All(cue, w => Assert.DoesNotContain("'", w)));
        // Longest-first ordering so specific cues win over their own prefixes.
        Assert.True(ReferenceCues.Phrases[0].Length == ReferenceCues.MaxWords);
    }

    // --- Verse cues: a bare number after "start from"/"begin at"/"pick up at" is a verse ---

    [Fact]
    public void VerseCue_ResolvesTheRomansSequence()
    {
        // Real transcript that missed the final verse before verse cues existed.
        var parser = new ReferenceParser();
        parser.Parse("let us look at");
        Assert.Equal("Romans 5", parser.Parse("romans chapter five").Single().ToString());
        parser.Parse("we're about to chapter eight let's go to chapter eight");
        Assert.Equal("Romans 8:5", parser.Parse("i want to read verse five to us but we can").Single().ToString());
        // The payoff: "we'll start from one" → Romans 8:1.
        Assert.Equal("Romans 8:1", parser.Parse("we'll start from one").Single().ToString());
    }

    [Theory]
    [InlineData("start at three", "Romans 8:3")]
    [InlineData("we'll start from one", "Romans 8:1")]
    [InlineData("let's begin at sixteen", "Romans 8:16")]
    [InlineData("we pick up at twelve", "Romans 8:12")]
    [InlineData("beginning at verse two", "Romans 8:2")]
    [InlineData("start from one through five", "Romans 8:1-5")]
    public void VerseCue_AttachesBareNumberAsVerse(string continuation, string expected)
    {
        var parser = new ReferenceParser();
        parser.Parse("romans chapter eight");
        Assert.Equal(expected, parser.Parse(continuation).Single().ToString());
    }

    [Fact]
    public void VerseCue_RequiresAnEstablishedChapter()
    {
        // A verse cue with no book/chapter in context must not invent a reference.
        var parser = new ReferenceParser();
        parser.Parse("let us look at");
        Assert.Empty(parser.Parse("we'll start from one"));
    }

    [Theory]
    [InlineData("four people came forward that day")] // bare number, no cue → prose
    [InlineData("we will start the service now")]     // "start" without "from"/"at" + number
    [InlineData("we sang about one hundred songs")]   // number, no verse cue
    public void VerseCue_DoesNotFireWithoutCue(string utterance)
    {
        var parser = new ReferenceParser();
        parser.Parse("romans chapter eight"); // Romans 8 in context
        Assert.Empty(parser.Parse(utterance));
    }

    [Fact]
    public void VerseCues_LibraryIsNonEmptyAndPunctuationFree()
    {
        Assert.NotEmpty(ReferenceCues.VersePhrases);
        Assert.All(ReferenceCues.VersePhrases, cue => Assert.All(cue, w => Assert.DoesNotContain("'", w)));
        Assert.True(ReferenceCues.VersePhrases[0].Length == ReferenceCues.VerseMaxWords);
    }
}
