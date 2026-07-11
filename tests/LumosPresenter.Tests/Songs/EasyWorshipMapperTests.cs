using LumosPresenter.Core.Songs;

namespace LumosPresenter.Tests.Songs;

public sealed class EasyWorshipMapperTests
{
    [Fact]
    public void Map_StripsRtfAndSplitsSections()
    {
        var row = new EasyWorshipSongRow(
            "Amazing Grace",
            "John Newton",
            "Public Domain",
            @"{\rtf1\ansi Verse 1\par Amazing grace\par\par Chorus\par My chains are gone}");

        var draft = EasyWorshipMapper.Map(row);

        Assert.NotNull(draft);
        Assert.Equal("Amazing Grace", draft.Title);
        Assert.Equal("John Newton", draft.Author);
        Assert.Equal("Public Domain", draft.Copyright);
        Assert.Equal("easyworship", draft.Source);
        Assert.Equal(2, draft.Sections.Count);
        Assert.Equal("Verse 1", draft.Sections[0].Label);
        Assert.Equal("Amazing grace", draft.Sections[0].Text);
        Assert.Equal("Chorus", draft.Sections[1].Label);
    }

    [Fact]
    public void Map_BlankTitle_ReturnsNull()
    {
        Assert.Null(EasyWorshipMapper.Map(new EasyWorshipSongRow("  ", null, null, @"{\rtf1 words}")));
    }

    [Fact]
    public void Map_NoLyrics_ReturnsNull()
    {
        Assert.Null(EasyWorshipMapper.Map(new EasyWorshipSongRow("Titled", null, null, @"{\rtf1\ansi }")));
    }

    [Fact]
    public void Map_TrimsMetadataAndNullsEmpty()
    {
        var draft = EasyWorshipMapper.Map(new EasyWorshipSongRow(
            "  Trimmed  ", "   ", "  © 2024  ", @"{\rtf1 a line}"));

        Assert.NotNull(draft);
        Assert.Equal("Trimmed", draft.Title);
        Assert.Null(draft.Author);
        Assert.Equal("© 2024", draft.Copyright);
    }
}
