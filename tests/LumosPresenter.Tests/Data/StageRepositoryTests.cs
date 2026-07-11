using LumosPresenter.Core.Domain;
using LumosPresenter.Data;
using LumosPresenter.Data.Migrations;
using LumosPresenter.Data.Seeding;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Tests.Data;

public sealed class StageRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly SqliteStageRepository _repository;

    public StageRepositoryTests()
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
        _initializer = new DatabaseInitializer(
            _factory, migrations, importer, options, NullLogger<DatabaseInitializer>.Instance);
        _repository = new SqliteStageRepository(_factory);
        _initializer.Initialize();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>Default with the scripture text block mutated — enough variation for link/snapshot tests.</summary>
    private static DisplayConfig WithScriptureText(Func<TextDisplayConfig, TextDisplayConfig> mutate) =>
        DisplayConfig.Default with
        {
            Scripture = DisplayConfig.Default.Scripture with { Text = mutate(DisplayConfig.Default.Scripture.Text) },
        };

    [Fact]
    public async Task Initialize_SeedsDefaultDisplayAndFonts()
    {
        var display = Assert.Single(await _repository.GetDisplaysAsync());
        Assert.Equal("Display 1", display.Name);
        Assert.Null(display.FollowsDisplayId);
        Assert.Equal(DisplayConfig.Default, display.Config);

        var fonts = await _repository.GetFontsAsync();
        Assert.Equal(8, fonts.Count);
        Assert.Equal("hanken-grotesk", fonts[0].Slug);
        Assert.Equal([400], Assert.Single(fonts, f => f.Slug == "bebas-neue").Weights);
    }

    [Fact]
    public async Task Initialize_IsIdempotent_ForStage()
    {
        _initializer.Initialize();
        Assert.Equal(1, await _repository.CountDisplaysAsync());
        Assert.Equal(8, (await _repository.GetFontsAsync()).Count);
    }

    [Fact]
    public async Task UpdateConfig_RoundtripsJson()
    {
        var display = Assert.Single(await _repository.GetDisplaysAsync());
        var config = new DisplayConfig(
            new ScriptureDisplayConfig(
                new TextDisplayConfig("eb-garamond", 500, 96, "#fefce8", "left", "bottom",
                    new BackgroundConfig("solid", "#1c1917"),
                    new ViewportRect(10.5, 20, 60, 45.5),
                    new PaddingConfig(48, 16, 8, 16)),
                new ReferenceConfig(false, "above-right", "inter", 600, 30, "#10b981")),
            new SongsDisplayConfig(
                new TextDisplayConfig("lora", 400, 80, "#fde68a", "center", "top",
                    new BackgroundConfig("solid", "#111827"),
                    new ViewportRect(0, 50, 100, 50),
                    new PaddingConfig(32, 32, 32, 32))),
            new MediaDisplayConfig("contain", "#101010", new ViewportRect(5, 5, 90, 90)));

        await _repository.UpdateConfigAsync(display.Id, config);

        Assert.Equal(config, (await _repository.GetDisplayAsync(display.Id))!.Config);
    }

    [Fact]
    public async Task UpdateConfig_UnknownDisplay_ReturnsNull()
    {
        Assert.Null(await _repository.UpdateConfigAsync(999, DisplayConfig.Default));
    }

    [Fact]
    public async Task LegacyFlatBlob_ResetsToDefault()
    {
        var display = Assert.Single(await _repository.GetDisplaysAsync());
        using (var connection = _factory.Open())
        {
            // A blob from before the per-type (scripture/songs/media) container.
            Dapper.SqlMapper.Execute(connection,
                "UPDATE displays SET config_json = @Json WHERE id = @Id",
                new
                {
                    display.Id,
                    Json = """{"fontSlug":"lora","fontWeight":400,"fontSizePx":80,"textColor":"#ffffff","horizontalAlign":"center","verticalAlign":"middle","reference":{"show":true,"position":"below-text","fontSlug":"jetbrains-mono","fontWeight":500,"fontSizePx":24,"color":"#adc6ff"},"background":{"type":"solid","color":"#020617"},"viewport":{"x":0,"y":0,"width":100,"height":100}}""",
                });
        }

        Assert.Equal(DisplayConfig.Default, (await _repository.GetDisplayAsync(display.Id))!.Config);
    }

    [Fact]
    public async Task LegacyReferencePosition_ResetsToDefaultPosition()
    {
        var display = Assert.Single(await _repository.GetDisplaysAsync());
        // A config from the old corner-pinned scheme: valid sub-configs but an obsolete position.
        var legacy = WithScriptureText(text => text) with
        {
            Scripture = DisplayConfig.Default.Scripture with
            {
                Reference = DisplayConfig.Default.Scripture.Reference with { Position = "top-right" },
            },
        };
        using (var connection = _factory.Open())
        {
            Dapper.SqlMapper.Execute(connection,
                "UPDATE displays SET config_json = @Json WHERE id = @Id",
                new
                {
                    display.Id,
                    Json = System.Text.Json.JsonSerializer.Serialize(
                        legacy, System.Text.Json.JsonSerializerOptions.Web),
                });
        }

        var config = (await _repository.GetDisplayAsync(display.Id))!.Config;
        Assert.Equal("below-center", config.Scripture.Reference.Position);
        // Everything else is untouched — only the obsolete position resets.
        Assert.Equal(DisplayConfig.Default.Scripture.Reference.Color, config.Scripture.Reference.Color);
    }

    [Fact]
    public async Task Follower_ResolvesSourceConfig()
    {
        var source = Assert.Single(await _repository.GetDisplaysAsync());
        var follower = await _repository.CreateDisplayAsync("Display 2", source.Config, source.Id);
        Assert.Equal(source.Id, follower.FollowsDisplayId);
        Assert.Equal(1, follower.SortOrder);

        var updated = WithScriptureText(text => text with { FontSizePx = 120 });
        await _repository.UpdateConfigAsync(source.Id, updated);

        Assert.Equal(updated, (await _repository.GetDisplayAsync(follower.Id))!.Config);
        Assert.Equal([follower.Id], await _repository.GetFollowerIdsAsync(source.Id));
    }

    [Fact]
    public async Task Detach_SnapshotsSourceConfig()
    {
        var source = Assert.Single(await _repository.GetDisplaysAsync());
        var sourceConfig = WithScriptureText(text => text with { TextColor = "#fbbf24" });
        await _repository.UpdateConfigAsync(source.Id, sourceConfig);
        var follower = await _repository.CreateDisplayAsync("Display 2", DisplayConfig.Default, source.Id);

        var detached = await _repository.SetFollowsAsync(follower.Id, null);

        Assert.Null(detached!.FollowsDisplayId);
        Assert.Equal(sourceConfig, detached.Config); // kept its look, now as its own config
        Assert.Empty(await _repository.GetFollowerIdsAsync(source.Id));

        // Source changes no longer affect the detached display.
        await _repository.UpdateConfigAsync(source.Id, WithScriptureText(text => text with { FontSizePx = 150 }));
        Assert.Equal(sourceConfig, (await _repository.GetDisplayAsync(follower.Id))!.Config);
    }

    [Fact]
    public async Task DeleteSource_DetachesFollowersWithSnapshot()
    {
        var source = Assert.Single(await _repository.GetDisplaysAsync());
        var sourceConfig = WithScriptureText(text => text with { FontSlug = "lora", FontWeight = 600 });
        await _repository.UpdateConfigAsync(source.Id, sourceConfig);
        var follower = await _repository.CreateDisplayAsync("Display 2", DisplayConfig.Default, source.Id);

        Assert.True(await _repository.DeleteDisplayAsync(source.Id));

        var remaining = (await _repository.GetDisplayAsync(follower.Id))!;
        Assert.Null(remaining.FollowsDisplayId);
        Assert.Equal(sourceConfig, remaining.Config);
        Assert.Equal(1, await _repository.CountDisplaysAsync());
    }

    [Fact]
    public async Task DeleteDisplay_RemovesRow()
    {
        var second = await _repository.CreateDisplayAsync("Display 2", DisplayConfig.Default);
        Assert.Equal(2, await _repository.CountDisplaysAsync());

        Assert.True(await _repository.DeleteDisplayAsync(second.Id));
        Assert.False(await _repository.DeleteDisplayAsync(second.Id));
        Assert.Null(await _repository.GetDisplayAsync(second.Id));
        Assert.Equal(1, await _repository.CountDisplaysAsync());
    }
}
