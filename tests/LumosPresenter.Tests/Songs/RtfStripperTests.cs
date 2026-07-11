using LumosPresenter.Core.Songs;

namespace LumosPresenter.Tests.Songs;

public sealed class RtfStripperTests
{
    [Fact]
    public void ToPlainText_ParagraphsBecomeNewlines()
    {
        const string rtf = @"{\rtf1\ansi line one\par line two\par}";
        Assert.Equal("line one\nline two", RtfStripper.ToPlainText(rtf));
    }

    [Fact]
    public void ToPlainText_LineBreakBecomesNewline()
    {
        const string rtf = @"{\rtf1 first\line second}";
        Assert.Equal("first\nsecond", RtfStripper.ToPlainText(rtf));
    }

    [Fact]
    public void ToPlainText_SkipsFontTable()
    {
        const string rtf = @"{\rtf1{\fonttbl{\f0 Arial;}}Amazing grace}";
        Assert.Equal("Amazing grace", RtfStripper.ToPlainText(rtf));
    }

    [Fact]
    public void ToPlainText_SkipsIgnorableDestination()
    {
        const string rtf = @"{\rtf1{\*\generator Riched20}real lyrics}";
        Assert.Equal("real lyrics", RtfStripper.ToPlainText(rtf));
    }

    [Fact]
    public void ToPlainText_DecodesHexEscape()
    {
        // \'92 is a curly apostrophe in Windows-1252.
        const string rtf = @"{\rtf1 it\'92s good}";
        Assert.Equal("it’s good", RtfStripper.ToPlainText(rtf));
    }

    [Fact]
    public void ToPlainText_DecodesUnicodeControl()
    {
        const string rtf = @"{\rtf1 \u233?cole}"; // é
        Assert.Equal("école", RtfStripper.ToPlainText(rtf).Replace("?", ""));
    }

    [Fact]
    public void ToPlainText_EscapedBraces()
    {
        const string rtf = @"{\rtf1 a \{b\} c}";
        Assert.Equal("a {b} c", RtfStripper.ToPlainText(rtf));
    }

    [Fact]
    public void ToPlainText_Empty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, RtfStripper.ToPlainText(""));
    }

    [Fact]
    public void ToPlainText_RealisticVerseAndChorus()
    {
        const string rtf =
            @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\f0 Verse 1\par Amazing grace how sweet the sound\par\par Chorus\par How great thou art\par}";
        var text = RtfStripper.ToPlainText(rtf);

        Assert.Contains("Verse 1\nAmazing grace how sweet the sound", text);
        Assert.Contains("Chorus\nHow great thou art", text);
        // Feeding the stripped text through the lyrics parser should yield two labeled sections.
        var sections = LyricsParser.Parse(text);
        Assert.Equal(2, sections.Count);
        Assert.Equal("Verse 1", sections[0].Label);
        Assert.Equal("Chorus", sections[1].Label);
    }
}
