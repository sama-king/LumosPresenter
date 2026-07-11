using LumosPresenter.Core.Songs;

namespace LumosPresenter.Tests.Songs;

public sealed class LyricsParserTests
{
    [Fact]
    public void Parse_BlankLineSeparated_SplitsSections()
    {
        var sections = LyricsParser.Parse("line one\nline two\n\nline three");

        Assert.Equal(2, sections.Count);
        Assert.Equal("line one\nline two", sections[0].Text);
        Assert.Equal("line three", sections[1].Text);
        Assert.Equal([0, 1], sections.Select(s => s.Position));
        Assert.All(sections, s => Assert.Null(s.Label));
    }

    [Fact]
    public void Parse_LabeledFirstLine_SetsLabelAndDropsIt()
    {
        var sections = LyricsParser.Parse("Verse 1\nAmazing grace\nhow sweet");

        var section = Assert.Single(sections);
        Assert.Equal("Verse 1", section.Label);
        Assert.Equal("Amazing grace\nhow sweet", section.Text);
    }

    [Theory]
    [InlineData("chorus:", "Chorus")]
    [InlineData("CHORUS", "Chorus")]
    [InlineData("verse  2", "Verse 2")]
    [InlineData("Pre-Chorus", "Pre-Chorus")]
    [InlineData("pre chorus", "Pre Chorus")]
    public void Parse_LabelWithColonAndCase_Normalizes(string first, string expected)
    {
        var section = Assert.Single(LyricsParser.Parse($"{first}\nsome words"));
        Assert.Equal(expected, section.Label);
    }

    [Fact]
    public void Parse_UnlabeledBlock_NullLabel()
    {
        var section = Assert.Single(LyricsParser.Parse("just some lyrics\nwith no label"));
        Assert.Null(section.Label);
        Assert.Equal("just some lyrics\nwith no label", section.Text);
    }

    [Fact]
    public void Parse_WindowsNewlinesAndExtraBlanks_Collapses()
    {
        var sections = LyricsParser.Parse("one\r\n\r\n\r\ntwo\r\n   \r\nthree");

        Assert.Equal(["one", "two", "three"], sections.Select(s => s.Text));
    }

    [Fact]
    public void Parse_LabelOnlyBlock_Dropped()
    {
        var sections = LyricsParser.Parse("Chorus\n\nreal lyrics here");

        // A block that is only a label carries no text — it is discarded, not kept empty.
        var section = Assert.Single(sections);
        Assert.Null(section.Label);
        Assert.Equal("real lyrics here", section.Text);
    }

    [Fact]
    public void Parse_Empty_ReturnsNoSections()
    {
        Assert.Empty(LyricsParser.Parse("   \n\n  "));
    }

    [Fact]
    public void Compose_ThenParse_RoundTrips()
    {
        const string lyrics = "Verse 1\nAmazing grace how sweet the sound\nthat saved a wretch like me\n\nChorus\nHow great thou art\n\nunlabeled outro line";

        var sections = LyricsParser.Parse(lyrics);
        var roundTripped = LyricsParser.Parse(LyricsParser.Compose(sections));

        Assert.Equal(
            sections.Select(s => (s.Position, s.Label, s.Text)),
            roundTripped.Select(s => (s.Position, s.Label, s.Text)));
    }
}
