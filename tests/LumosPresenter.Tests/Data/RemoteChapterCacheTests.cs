using Dapper;
using LumosPresenter.Core.Abstractions;
using LumosPresenter.Core.Domain;
using LumosPresenter.Data;
using LumosPresenter.Data.Migrations;
using LumosPresenter.Data.Remote;
using LumosPresenter.Data.Seeding;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Tests.Data;

/// <summary>
/// Cache behaviour for remote (api.bible) translations: fused verse spans, the sliding
/// 14-day window, and the purge of expired chapters.
/// </summary>
public sealed class RemoteChapterCacheTests : IDisposable
{
    private const int RomansBookNumber = 45;

    private readonly string _tempDir;
    private readonly SqliteConnectionFactory _factory;
    private readonly RemoteChapterCache _cache;
    private readonly SqliteVerseRepository _repository;

    public RemoteChapterCacheTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-remote-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "seed"));
        var options = Options.Create(new DataOptions
        {
            DatabasePath = Path.Combine(_tempDir, "lumos.db"),
            SeedDirectory = Path.Combine(_tempDir, "seed"),
        });
        _factory = new SqliteConnectionFactory(options);
        var apiOptions = Options.Create(new ApiBibleOptions());
        _cache = new RemoteChapterCache(_factory, apiOptions, NullLogger<RemoteChapterCache>.Instance);

        var remote = new OfflineScriptureSource
        {
            Translations = [new Translation("MSG", "The Message", "en")],
        };
        new DatabaseInitializer(
            _factory,
            new MigrationRunner(_factory, NullLogger<MigrationRunner>.Instance),
            new ScrollmapperImporter(_factory, NullLogger<ScrollmapperImporter>.Instance),
            remote, _cache, new SqliteAppSettings(_factory), options, apiOptions,
            NullLogger<DatabaseInitializer>.Instance)
            .Initialize();
        _repository = new SqliteVerseRepository(_factory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDir, recursive: true);
    }

    private static RemoteChapter MessageRomans8() => new(
        "MSG", RomansBookNumber, 8,
        [
            new RemoteVerse(1, 2, "There is no condemnation for those in Christ."),
            new RemoteVerse(5, 8, "Those who trust God's action in them."),
        ],
        "Copyright © The Message");

    [Fact]
    public void SeedsRemoteTranslationsSoThePickerOffersThem()
    {
        using var connection = _factory.Open();
        var source = connection.ExecuteScalar<string>("SELECT source FROM translations WHERE code = 'MSG'");
        Assert.Equal("api.bible", source);
    }

    [Fact]
    public async Task ResolvesAnyVerseInsideAFusedSpan()
    {
        _cache.Save(MessageRomans8());

        // Verse 6 sits inside the 5-8 block: a paraphrase must still answer for it.
        var verses = await _repository.GetVersesAsync("MSG", new BibleReference("Romans", 8, 6, null, 1.0));

        var verse = Assert.Single(verses);
        Assert.Equal("Those who trust God's action in them.", verse.Text);
    }

    [Fact]
    public async Task ChapterLookupReturnsEverySpanInOrder()
    {
        _cache.Save(MessageRomans8());

        var verses = await _repository.GetVersesAsync("MSG", new BibleReference("Romans", 8, null, null, 1.0));

        Assert.Equal(2, verses.Count);
        Assert.Equal([1, 5], verses.Select(v => v.Number).ToArray());
    }

    [Fact]
    public void SaveRecordsCopyrightAsTheLicence()
    {
        _cache.Save(MessageRomans8());

        using var connection = _factory.Open();
        var license = connection.ExecuteScalar<string>("SELECT license FROM translations WHERE code = 'MSG'");
        Assert.Equal("Copyright © The Message", license);
    }

    [Fact]
    public void FreshAfterSaveAndStaleOnceTheWindowElapses()
    {
        _cache.Save(MessageRomans8());
        Assert.True(_cache.IsFresh("MSG", RomansBookNumber, 8));

        Expire();
        Assert.False(_cache.IsFresh("MSG", RomansBookNumber, 8));
    }

    [Fact]
    public void TouchSlidesTheWindowForward()
    {
        _cache.Save(MessageRomans8());
        Expire();
        Assert.False(_cache.IsFresh("MSG", RomansBookNumber, 8));

        // A new reference to the chapter renews it rather than forcing a re-fetch.
        _cache.Touch("MSG", RomansBookNumber, 8);

        Assert.True(_cache.IsFresh("MSG", RomansBookNumber, 8));
    }

    [Fact]
    public async Task PurgeRemovesExpiredVersesLeavingNoOrphans()
    {
        _cache.Save(MessageRomans8());
        Expire();

        Assert.Equal(1, _cache.PurgeExpired());

        // Verse rows must go with the tracking row: GetVersesAsync has no freshness
        // check, so anything left behind would be served forever.
        var verses = await _repository.GetVersesAsync("MSG", new BibleReference("Romans", 8, null, null, 1.0));
        Assert.Empty(verses);
        using var connection = _factory.Open();
        Assert.Equal(0, connection.ExecuteScalar<int>("SELECT COUNT(*) FROM remote_chapters"));
    }

    [Fact]
    public void PurgeKeepsChaptersThatAreStillFresh()
    {
        _cache.Save(MessageRomans8());

        Assert.Equal(0, _cache.PurgeExpired());
        Assert.True(_cache.IsFresh("MSG", RomansBookNumber, 8));
    }

    [Fact]
    public void TouchRangeRenewsEveryChapterInTheWindowAtOnce()
    {
        // The prefetch window is chapter ±1; renewing it must not need a query per chapter.
        _cache.Save(MessageRomans8() with { Chapter = 7 });
        _cache.Save(MessageRomans8());
        _cache.Save(MessageRomans8() with { Chapter = 9 });
        Expire();

        _cache.TouchRange(["MSG"], RomansBookNumber, 7, 9);

        Assert.True(_cache.IsFresh("MSG", RomansBookNumber, 7));
        Assert.True(_cache.IsFresh("MSG", RomansBookNumber, 8));
        Assert.True(_cache.IsFresh("MSG", RomansBookNumber, 9));
    }

    [Fact]
    public void FreshChaptersReportsOnlyWhatIsCached()
    {
        _cache.Save(MessageRomans8());

        var fresh = _cache.FreshChapters("MSG", RomansBookNumber, 7, 9);

        Assert.Equal([8], fresh.Order().ToArray());
    }

    /// <summary>Ages the cached chapter past its window, as 14 days would.</summary>
    private void Expire()
    {
        using var connection = _factory.Open();
        connection.Execute("UPDATE remote_chapters SET expires_at = datetime('now', '-1 day')");
    }
}
