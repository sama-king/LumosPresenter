using LumosPresenter.Core.Domain;
using LumosPresenter.Core.Parsing;

namespace LumosPresenter.Tests.Parsing;

public class TranslationDetectorTests
{
    private static TranslationDetector Detector(params (string Code, string Name)[] translations)
    {
        var detector = new TranslationDetector();
        detector.Configure(translations.Select(t => new Translation(t.Code, t.Name, "en")));
        return detector;
    }

    private static TranslationDetector Seeded() => Detector(
        ("KJV", "King James Version"),
        ("ASV", "American Standard Version"),
        ("BSB", "Berean Standard Bible"));

    // --- Bare mention: multi-word names are distinctive enough on their own ---

    [Theory]
    [InlineData("the king james renders this beautifully", "KJV")]
    [InlineData("let's read from the king james version", "KJV")]
    [InlineData("the berean standard bible puts it this way", "BSB")]
    [InlineData("the berean standard puts it this way", "BSB")]
    [InlineData("the american standard says otherwise", "ASV")]
    public void MultiWordName_MatchesOnBareMention(string utterance, string expected)
    {
        Assert.Equal(expected, Seeded().Detect(utterance));
    }

    // --- Letter codes: only with a preceding cue phrase ---

    [Theory]
    [InlineData("switch to the kjv", "KJV")]
    [InlineData("let's use the bsb for this one", "BSB")]
    [InlineData("reading from the asv tonight", "ASV")]
    [InlineData("switch to the k j v", "KJV")]
    public void LetterCode_MatchesWithCue(string utterance, string expected)
    {
        Assert.Equal(expected, Seeded().Detect(utterance));
    }

    [Theory]
    [InlineData("kjv is what my grandmother always read")]
    [InlineData("he mentioned bsb somewhere in passing")]
    [InlineData("the letters k j v were on the spine")]
    public void LetterCode_WithoutCue_DoesNotMatch(string utterance)
    {
        Assert.Null(Seeded().Detect(utterance));
    }

    // --- Single distinctive word: cue-gated, for names that reduce to one word ---

    [Theory]
    [InlineData("now let's take this from the amplified", "AMP")]
    [InlineData("switch to the amplified", "AMP")]
    [InlineData("reading from the amplified bible", "AMP")]
    [InlineData("let's use the message here", "MSG")]
    [InlineData("the message puts it this way", "MSG")]
    public void SingleDistinctiveWord_MatchesWithCue(string utterance, string expected)
    {
        var detector = Detector(("AMP", "Amplified Bible"), ("MSG", "The Message"));
        Assert.Equal(expected, detector.Detect(utterance));
    }

    [Theory]
    [InlineData("her testimony was amplified by the choir")]
    [InlineData("that message stayed with me all week")]
    public void SingleDistinctiveWord_WithoutCue_DoesNotMatch(string utterance)
    {
        var detector = Detector(("AMP", "Amplified Bible"), ("MSG", "The Message"));
        Assert.Null(detector.Detect(utterance));
    }

    [Theory]
    [InlineData("let's use the new arrangement", "NIV", "New International Version")]
    [InlineData("switch to the king of kings", "KJV", "King James Version")]
    [InlineData("read from the american dream", "ASV", "American Standard Version")]
    [InlineData("in the berean tradition", "BSB", "Berean Standard Bible")]
    public void MultiWordName_DoesNotReduceToItsFirstWord(string utterance, string code, string name)
    {
        Assert.Null(Detector((code, name)).Detect(utterance));
    }

    // --- Last mention wins ---

    [Fact]
    public void LastMentionWins()
    {
        Assert.Equal("BSB", Seeded().Detect("not the king james let's use the berean standard"));
    }

    // --- No false positives ---

    [Theory]
    [InlineData("hold yourselves to a high standard")]
    [InlineData("james chapter one verse two")]           // book name, not "king james"
    [InlineData("the new international version says so")] // not configured
    [InlineData("turn to john three sixteen")]
    [InlineData("")]
    public void UnrelatedProse_DoesNotMatch(string utterance)
    {
        Assert.Null(Seeded().Detect(utterance));
    }

    // --- DB-driven: only configured translations are detectable ---

    [Fact]
    public void OnlyConfiguredTranslationsDetect()
    {
        var detector = Detector(("WEB", "World English Bible"));
        Assert.Equal("WEB", detector.Detect("reading from the world english bible"));
        Assert.Null(detector.Detect("the king james says"));
    }

    [Fact]
    public void Unconfigured_DetectsNothing()
    {
        Assert.Null(new TranslationDetector().Detect("switch to the kjv"));
        Assert.False(new TranslationDetector().IsConfigured);
    }

    [Fact]
    public void Reconfigure_ReplacesAliases()
    {
        var detector = Detector(("KJV", "King James Version"));
        detector.Configure([new Translation("BSB", "Berean Standard Bible", "en")]);
        Assert.Null(detector.Detect("the king james says"));
        Assert.Equal("BSB", detector.Detect("the berean standard says"));
    }
}
