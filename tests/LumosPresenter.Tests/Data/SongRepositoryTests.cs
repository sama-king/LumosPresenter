using LumosPresenter.Core.Domain;
using LumosPresenter.Data;
using LumosPresenter.Data.Migrations;
using LumosPresenter.Data.Seeding;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Tests.Data;

public sealed class SongRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteSongRepository _repository;

    public SongRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new DataOptions
        {
            DatabasePath = Path.Combine(_tempDir, "lumos.db"),
            SeedDirectory = _tempDir, // no seed files: translation seeding logs and skips
        });
        _factory = new SqliteConnectionFactory(options);
        var migrations = new MigrationRunner(_factory, NullLogger<MigrationRunner>.Instance);
        var importer = new ScrollmapperImporter(_factory, NullLogger<ScrollmapperImporter>.Instance);
        new DatabaseInitializer(_factory, migrations, importer, options, NullLogger<DatabaseInitializer>.Instance)
            .Initialize();
        _repository = new SqliteSongRepository(_factory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDir, recursive: true);
    }

    private static SongDraft Draft(string title, params (string? Label, string Text)[] sections) => new(
        title, "Author", "© 2024", [.. sections.Select((s, i) => new SongSection(i, s.Label, s.Text))]);

    [Fact]
    public async Task Create_ReturnsSongWithIdAndSections()
    {
        var created = await _repository.CreateAsync(
            Draft("Amazing Grace", ("Verse 1", "Amazing grace"), (null, "How sweet the sound")));

        Assert.True(created.Id > 0);
        Assert.Equal("Amazing Grace", created.Title);
        Assert.Equal("Author", created.Author);
        Assert.Equal(2, created.Sections.Count);
        Assert.Equal("Verse 1", created.Sections[0].Label);
        Assert.Null(created.Sections[1].Label);
        Assert.Equal([0, 1], created.Sections.Select(s => s.Position));
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsNull()
    {
        Assert.Null(await _repository.GetAsync(999));
    }

    [Fact]
    public async Task Update_ReplacesFieldsAndSections()
    {
        var created = await _repository.CreateAsync(Draft("Original", ("Verse 1", "one"), ("Verse 2", "two")));

        var updated = await _repository.UpdateAsync(created.Id,
            new SongDraft("Renamed", "New Author", null, [new SongSection(0, "Chorus", "only one now")]));

        Assert.NotNull(updated);
        Assert.Equal("Renamed", updated.Title);
        Assert.Equal("New Author", updated.Author);
        Assert.Null(updated.Copyright);
        Assert.Equal("Chorus", Assert.Single(updated.Sections).Label);
        // Re-read to confirm the replacement persisted (no leftover rows from the two-section original).
        Assert.Equal("only one now", Assert.Single((await _repository.GetAsync(created.Id))!.Sections).Text);
    }

    [Fact]
    public async Task Update_UnknownId_ReturnsNull()
    {
        Assert.Null(await _repository.UpdateAsync(999, Draft("Nope", (null, "x"))));
    }

    [Fact]
    public async Task Delete_RemovesSongAndSections()
    {
        var created = await _repository.CreateAsync(Draft("Temp", ("Verse 1", "gone soon")));

        Assert.True(await _repository.DeleteAsync(created.Id));
        Assert.False(await _repository.DeleteAsync(created.Id));
        Assert.Null(await _repository.GetAsync(created.Id));

        using var connection = _factory.Open();
        var orphans = Dapper.SqlMapper.ExecuteScalar<int>(connection,
            "SELECT COUNT(*) FROM song_sections WHERE song_id = @Id", new { created.Id });
        Assert.Equal(0, orphans);
    }

    [Fact]
    public async Task Search_TitleSubstring_CaseInsensitive()
    {
        await _repository.CreateAsync(Draft("Amazing Grace", (null, "a")));
        await _repository.CreateAsync(Draft("Great Is Thy Faithfulness", (null, "b")));

        var results = await _repository.SearchAsync("grace");
        Assert.Equal("Amazing Grace", Assert.Single(results).Title);
    }

    [Fact]
    public async Task Search_Blank_ReturnsAllOrderedByTitle()
    {
        await _repository.CreateAsync(Draft("Zion", (null, "a")));
        await _repository.CreateAsync(Draft("Abide With Me", (null, "b")));

        var results = await _repository.SearchAsync("  ");
        Assert.Equal(["Abide With Me", "Zion"], results.Select(r => r.Title));
    }
}
