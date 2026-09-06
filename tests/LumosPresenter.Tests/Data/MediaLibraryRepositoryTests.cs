using LumosPresenter.Data;
using LumosPresenter.Data.Remote;
using LumosPresenter.Data.Migrations;
using LumosPresenter.Data.Seeding;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Tests.Data;

/// <summary>
/// The media gallery links files rather than copying them, so the behaviour worth pinning is
/// what happens around the path: adding the same one twice, and what a row reports once the
/// file it points at is gone.
/// </summary>
public sealed class MediaLibraryRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnectionFactory _factory;
    private readonly SqliteMediaLibraryRepository _repository;

    public MediaLibraryRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new DataOptions
        {
            DatabasePath = Path.Combine(_tempDir, "lumos.db"),
            SeedDirectory = _tempDir,
        });
        _factory = new SqliteConnectionFactory(options);
        var migrations = new MigrationRunner(_factory, NullLogger<MigrationRunner>.Instance);
        var importer = new ScrollmapperImporter(_factory, NullLogger<ScrollmapperImporter>.Instance);
        new DatabaseInitializer(_factory, migrations, importer, new OfflineScriptureSource(),
            new RemoteChapterCache(_factory, Options.Create(new ApiBibleOptions()), NullLogger<RemoteChapterCache>.Instance),
            new SqliteAppSettings(_factory),
            options, Options.Create(new ApiBibleOptions()), NullLogger<DatabaseInitializer>.Instance)
            .Initialize();
        _repository = new SqliteMediaLibraryRepository(_factory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>Writes a real file so Exists resolves true; content is irrelevant.</summary>
    private string WriteFile(string name)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public async Task Add_LinksFileWithoutCopyingIt()
    {
        var path = WriteFile("slide.png");

        var item = await _repository.AddAsync(path, "image", "slide", "image/png");

        Assert.Equal(path, item.SourcePath);
        Assert.Equal("image", item.Kind);
        Assert.True(item.Exists);
        // The file stays exactly where it was — nothing is written into a media directory.
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Add_SamePathTwice_ReturnsTheExistingRow()
    {
        var path = WriteFile("repeat.jpg");

        var first = await _repository.AddAsync(path, "image", "repeat", "image/jpeg");
        var second = await _repository.AddAsync(path, "image", "repeat", "image/jpeg");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await _repository.GetAllAsync());
    }

    [Fact]
    public async Task GetAll_ReportsExistsFalse_WhenTheFileHasMoved()
    {
        var path = WriteFile("gone.mp4");
        await _repository.AddAsync(path, "video", "gone", "video/mp4");

        File.Delete(path);

        var item = Assert.Single(await _repository.GetAllAsync());
        // The row survives so the operator can see and clear it; only Exists flips, which is
        // what drives the gallery's error thumbnail.
        Assert.False(item.Exists);
        Assert.Equal(path, item.SourcePath);
    }

    [Fact]
    public async Task Delete_RemovesTheRowButNotTheFile()
    {
        var path = WriteFile("keep.png");
        var item = await _repository.AddAsync(path, "image", "keep", "image/png");

        Assert.True(await _repository.DeleteAsync(item.Id));

        Assert.Empty(await _repository.GetAllAsync());
        Assert.Null(await _repository.GetAsync(item.Id));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Delete_UnknownId_ReturnsFalse()
    {
        Assert.False(await _repository.DeleteAsync("nope"));
    }
}
