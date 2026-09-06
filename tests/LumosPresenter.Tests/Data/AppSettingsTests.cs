using LumosPresenter.Core.Abstractions;
using LumosPresenter.Data;
using LumosPresenter.Data.Migrations;
using LumosPresenter.Data.Remote;
using LumosPresenter.Data.Seeding;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Tests.Data;

/// <summary>
/// The api.bible key is a per-installation setting the operator owns, so it lives in the
/// database rather than in configuration. These cover the round trip, the "clear means
/// remove" contract the UI's Remove button relies on, and the one-time adoption of a key
/// that was configured before the move.
/// </summary>
public sealed class AppSettingsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnectionFactory _factory;
    private readonly IOptions<DataOptions> _dataOptions;

    public AppSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dataOptions = Options.Create(new DataOptions
        {
            DatabasePath = Path.Combine(_tempDir, "lumos.db"),
            SeedDirectory = _tempDir, // no seed files: translation seeding logs and skips
        });
        _factory = new SqliteConnectionFactory(_dataOptions);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>Runs migrations and seeding the way the app does, with the given config key.</summary>
    private SqliteAppSettings Initialize(string? configuredKey)
    {
        var settings = new SqliteAppSettings(_factory);
        new DatabaseInitializer(
            _factory,
            new MigrationRunner(_factory, NullLogger<MigrationRunner>.Instance),
            new ScrollmapperImporter(_factory, NullLogger<ScrollmapperImporter>.Instance),
            new OfflineScriptureSource(),
            new RemoteChapterCache(_factory, Options.Create(new ApiBibleOptions()), NullLogger<RemoteChapterCache>.Instance),
            settings,
            _dataOptions,
            Options.Create(new ApiBibleOptions { Key = configuredKey }),
            NullLogger<DatabaseInitializer>.Instance)
            .Initialize();
        return settings;
    }

    [Fact]
    public void StoresAndReadsBackASetting()
    {
        var settings = Initialize(configuredKey: null);
        settings.Set(AppSettingKeys.ApiBibleKey, "abc123");

        Assert.Equal("abc123", settings.Get(AppSettingKeys.ApiBibleKey));
        // A fresh instance reads the database, not the first one's cache.
        Assert.Equal("abc123", new SqliteAppSettings(_factory).Get(AppSettingKeys.ApiBibleKey));
    }

    [Fact]
    public void ClearingASettingRemovesIt()
    {
        var settings = Initialize(configuredKey: null);
        settings.Set(AppSettingKeys.ApiBibleKey, "abc123");
        settings.Set(AppSettingKeys.ApiBibleKey, null);

        Assert.Null(settings.Get(AppSettingKeys.ApiBibleKey));
        Assert.Null(new SqliteAppSettings(_factory).Get(AppSettingKeys.ApiBibleKey));
    }

    [Fact]
    public void AdoptsAConfiguredKeyOnADatabaseThatHasNone()
    {
        var settings = Initialize(configuredKey: "from-dot-env");

        Assert.Equal("from-dot-env", settings.Get(AppSettingKeys.ApiBibleKey));
    }

    [Fact]
    public void AStoredKeyWinsOverConfiguration()
    {
        Initialize(configuredKey: null).Set(AppSettingKeys.ApiBibleKey, "entered-in-the-ui");

        // Restart with a stale .env still on the machine: the operator's key stands.
        Assert.Equal("entered-in-the-ui", Initialize(configuredKey: "from-dot-env").Get(AppSettingKeys.ApiBibleKey));
    }

    [Fact]
    public void ClearingTheKeyIsNotUndoneByAStaleEnvOnRestart()
    {
        Initialize(configuredKey: "from-dot-env").Set(AppSettingKeys.ApiBibleKey, null);

        // Adoption is one-time, so removing the key in the UI turns online sources off for
        // good rather than resurrecting itself at every startup.
        Assert.Null(Initialize(configuredKey: "from-dot-env").Get(AppSettingKeys.ApiBibleKey));
    }
}
