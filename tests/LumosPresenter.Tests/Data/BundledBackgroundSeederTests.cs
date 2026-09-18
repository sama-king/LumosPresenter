using LumosPresenter.Core.Abstractions;
using LumosPresenter.Data;
using LumosPresenter.Data.Migrations;
using LumosPresenter.Data.Remote;
using LumosPresenter.Data.Seeding;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Tests.Data;

/// <summary>
/// The seeder's job is mostly about telling situations apart: never seeded, seeded then
/// deleted by the operator, and seeded but missing its file. Each gets its own test, since
/// getting any one wrong is a visible bug — a default that will not stay deleted, or a
/// broken thumbnail in a fresh install.
/// </summary>
public sealed class BundledBackgroundSeederTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _sourceDir;
    private readonly string _mediaDir;
    private readonly SqliteStageRepository _stage;
    private readonly SqliteAppSettings _settings;
    private readonly BundledBackgroundSeeder _seeder;

    public BundledBackgroundSeederTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-tests-" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_tempDir, "backgrounds");
        _mediaDir = Path.Combine(_tempDir, "media");
        Directory.CreateDirectory(_sourceDir);

        var options = Options.Create(new DataOptions
        {
            DatabasePath = Path.Combine(_tempDir, "lumos.db"),
            SeedDirectory = _tempDir, // no seed files: translation seeding logs and skips
        });
        var factory = new SqliteConnectionFactory(options);
        _settings = new SqliteAppSettings(factory);
        new DatabaseInitializer(
            factory,
            new MigrationRunner(factory, NullLogger<MigrationRunner>.Instance),
            new ScrollmapperImporter(factory, NullLogger<ScrollmapperImporter>.Instance),
            new OfflineScriptureSource(),
            new RemoteChapterCache(factory, Options.Create(new ApiBibleOptions()), NullLogger<RemoteChapterCache>.Instance),
            _settings,
            options, Options.Create(new ApiBibleOptions()), NullLogger<DatabaseInitializer>.Instance).Initialize();

        _stage = new SqliteStageRepository(factory);
        _seeder = new BundledBackgroundSeeder(
            _stage, _settings, new TestEnvironment(_tempDir), NullLogger<BundledBackgroundSeeder>.Instance);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task RegistersEachShippedImageAndCopiesItIntoMedia()
    {
        Ship("mountain-lake-reflection.jpg");
        Ship("blue-particles.png");
        Ship("README.md"); // not an image: must be ignored, not registered

        var added = await _seeder.SeedAsync(_sourceDir, _mediaDir);

        Assert.Equal(2, added);
        var assets = await _stage.GetMediaAssetsAsync();
        Assert.Equal(2, assets.Count);

        var mountain = Assert.Single(assets, a => a.Id == "bundled-mountain-lake-reflection");
        Assert.Equal("Mountain Lake Reflection", mountain.Title);
        Assert.Equal("image", mountain.Kind);
        Assert.Equal("bundled", mountain.Source);
        Assert.Equal(".jpg", mountain.FileExt);
        Assert.Equal("image/jpeg", mountain.ContentType);
        // Served from media/<id><ext>, exactly like an uploaded background.
        Assert.True(File.Exists(Path.Combine(_mediaDir, "bundled-mountain-lake-reflection.jpg")));

        Assert.Equal("image/png", Assert.Single(assets, a => a.Id == "bundled-blue-particles").ContentType);
    }

    [Fact]
    public async Task RunningAgainAddsNothing()
    {
        Ship("green-gradient.jpg");
        await _seeder.SeedAsync(_sourceDir, _mediaDir);

        var added = await _seeder.SeedAsync(_sourceDir, _mediaDir);

        Assert.Equal(0, added);
        Assert.Single(await _stage.GetMediaAssetsAsync());
    }

    [Fact]
    public async Task ADefaultTheOperatorDeletedStaysDeleted()
    {
        Ship("slate-cross.jpg");
        await _seeder.SeedAsync(_sourceDir, _mediaDir);
        await _stage.DeleteMediaAssetAsync("bundled-slate-cross");

        var added = await _seeder.SeedAsync(_sourceDir, _mediaDir);

        Assert.Equal(0, added);
        Assert.Empty(await _stage.GetMediaAssetsAsync());
    }

    [Fact]
    public async Task RestoresTheFileWhenOnlyTheRowSurvived()
    {
        // The package ships the build machine's database but not its media/ folder, so a
        // fresh install can hold the row with no file behind it.
        Ship("bokeh-lights.jpg");
        await _seeder.SeedAsync(_sourceDir, _mediaDir);
        var stored = Path.Combine(_mediaDir, "bundled-bokeh-lights.jpg");
        File.Delete(stored);

        var added = await _seeder.SeedAsync(_sourceDir, _mediaDir);

        Assert.Equal(0, added); // restored, not re-added
        Assert.True(File.Exists(stored));
        Assert.Single(await _stage.GetMediaAssetsAsync());
    }

    [Fact]
    public async Task ANewDefaultInALaterReleaseReachesAnExistingInstall()
    {
        Ship("rainbow-streaks.jpg");
        await _seeder.SeedAsync(_sourceDir, _mediaDir);

        Ship("warm-spectrum-streaks.jpg"); // shipped by the next release
        var added = await _seeder.SeedAsync(_sourceDir, _mediaDir);

        Assert.Equal(1, added);
        Assert.Equal(2, (await _stage.GetMediaAssetsAsync()).Count);
        Assert.Contains("bundled-warm-spectrum-streaks",
            _settings.Get(AppSettingKeys.BundledBackgroundsSeeded) ?? string.Empty);
    }

    [Fact]
    public async Task VideoShipsAsAMotionBackground()
    {
        Ship("worship-loop.mp4");

        await _seeder.SeedAsync(_sourceDir, _mediaDir);

        var asset = Assert.Single(await _stage.GetMediaAssetsAsync());
        Assert.Equal("motion", asset.Kind);
        Assert.Equal("video/mp4", asset.ContentType);
    }

    [Fact]
    public async Task AMissingFolderIsNotAnError()
    {
        // Development runs and older packages have no backgrounds/ folder at all.
        var added = await _seeder.SeedAsync(Path.Combine(_tempDir, "absent"), _mediaDir);

        Assert.Equal(0, added);
        Assert.Empty(await _stage.GetMediaAssetsAsync());
    }

    [Theory]
    [InlineData("mountain-lake-reflection", "mountain-lake-reflection")]
    [InlineData("Blue Particles (2)", "blue-particles-2")]
    [InlineData("  ", "background")]
    public void SlugIsStableAndUrlSafe(string name, string expected) =>
        Assert.Equal(expected, BundledBackgroundSeeder.Slug(name));

    private void Ship(string fileName) =>
        File.WriteAllBytes(Path.Combine(_sourceDir, fileName), [0xFF, 0xD8, 0xFF, 0xD9]);

    private sealed class TestEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "LumosPresenter.Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
