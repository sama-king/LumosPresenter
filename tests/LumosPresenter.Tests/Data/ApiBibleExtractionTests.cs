using System.Text.Json;
using LumosPresenter.Data.Remote;

namespace LumosPresenter.Tests.Data;

/// <summary>
/// Parsing of api.bible's chapter JSON. The shapes below are trimmed copies of real
/// responses — the verse-number markers and the fused verseOrgIds spans are exactly
/// what the provider sends.
/// </summary>
public sealed class ApiBibleExtractionTests
{
    private static List<Core.Abstractions.RemoteVerse> Extract(string json) =>
        ApiBibleScriptureSource.ExtractVerses(JsonDocument.Parse(json).RootElement);

    [Fact]
    public void ReadsPlainOneToOneVerses()
    {
        // NIV/AMP shape: each text node carries a single verseId.
        var verses = Extract("""
            {"content":[{"name":"para","type":"tag","items":[
              {"text":"Now there was a Pharisee. ","type":"text",
               "attrs":{"verseId":"JHN.3.1","verseOrgIds":["JHN.3.1"]}},
              {"text":"He came to Jesus at night.","type":"text",
               "attrs":{"verseId":"JHN.3.2","verseOrgIds":["JHN.3.2"]}}]}]}
            """);

        Assert.Equal(2, verses.Count);
        Assert.Equal("Now there was a Pharisee.", verses[0].Text);
        // A single verse spans only itself, so it stores no span end.
        Assert.Equal(1, verses[0].SpanEnd);
        Assert.Equal(2, verses[1].Verse);
    }

    [Fact]
    public void CollapsesFusedVersesIntoOneSpan()
    {
        // MSG shape: one block of text covering Romans 8:5-8.
        var verses = Extract("""
            {"content":[{"name":"para","type":"tag","items":[
              {"text":"Those who trust God's action in them.","type":"text",
               "attrs":{"verseId":"ROM.8.5-ROM.8.8",
                        "verseOrgIds":["ROM.8.5","ROM.8.6","ROM.8.7","ROM.8.8"]}}]}]}
            """);

        var span = Assert.Single(verses);
        Assert.Equal(5, span.Verse);
        Assert.Equal(8, span.SpanEnd);
    }

    [Fact]
    public void SkipsVerseNumberMarkers()
    {
        // The "verse" tags are printed markers whose text is the number itself; including
        // them would yield "1Now there was a Pharisee".
        var verses = Extract("""
            {"content":[{"name":"para","type":"tag","items":[
              {"name":"verse","type":"tag","attrs":{"number":"1","style":"v"},
               "items":[{"text":"1","type":"text"}]},
              {"text":"Now there was a Pharisee.","type":"text",
               "attrs":{"verseId":"JHN.3.1","verseOrgIds":["JHN.3.1"]}}]}]}
            """);

        var verse = Assert.Single(verses);
        Assert.Equal("Now there was a Pharisee.", verse.Text);
    }

    [Fact]
    public void JoinsTextNodesBelongingToTheSameVerse()
    {
        // AMP splits a verse across several nodes (bracketed amplifications).
        var verses = Extract("""
            {"content":[{"name":"para","type":"tag","items":[
              {"text":"Therefore there is now no condemnation ","type":"text",
               "attrs":{"verseId":"ROM.8.1","verseOrgIds":["ROM.8.1"]}},
              {"text":"[no guilty verdict] ","type":"text",
               "attrs":{"verseId":"ROM.8.1","verseOrgIds":["ROM.8.1"]}},
              {"text":"for those who are in Christ Jesus.","type":"text",
               "attrs":{"verseId":"ROM.8.1","verseOrgIds":["ROM.8.1"]}}]}]}
            """);

        var verse = Assert.Single(verses);
        Assert.Equal("Therefore there is now no condemnation [no guilty verdict] for those who are in Christ Jesus.",
            verse.Text);
    }

    [Fact]
    public void IgnoresContentWithoutVerseAttribution()
    {
        // Section headings and notes carry no verseId and are not scripture.
        var verses = Extract("""
            {"content":[{"name":"para","type":"tag","attrs":{"style":"s1"},
               "items":[{"text":"Life Through the Spirit","type":"text"}]}]}
            """);

        Assert.Empty(verses);
    }
}
