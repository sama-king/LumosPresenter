using Dapper;
using LumosPresenter.Core.Parsing;
using LumosPresenter.Data.Migrations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Data.Seeding;

/// <summary>
/// Startup initialization: apply migrations, sync canonical books from the parser's
/// BookCatalog, and seed bundled translations from scrollmapper files on first run.
/// Missing seed files are logged and skipped (tests and CI run without them).
/// </summary>
public sealed class DatabaseInitializer(
    SqliteConnectionFactory connectionFactory,
    MigrationRunner migrations,
    ScrollmapperImporter importer,
    IOptions<DataOptions> options,
    ILogger<DatabaseInitializer> logger)
{
    private static readonly (string Code, string Name, string Language, string License)[] Bundled =
    [
        ("KJV", "King James Version", "en", "public-domain"),
        ("ASV", "American Standard Version", "en", "public-domain"),
        ("BSB", "Berean Standard Bible", "en", "public-domain (CC0)"),
    ];

    public void Initialize()
    {
        migrations.Apply();
        SyncBooks();
        SeedBundledTranslations();
        SeedDefaultDisplay();
    }

    private void SyncBooks()
    {
        using var connection = connectionFactory.Open();
        foreach (var book in BookCatalog.Books)
        {
            connection.Execute("""
                INSERT INTO books (number, name, chapter_count)
                VALUES (@Number, @Name, @ChapterCount)
                ON CONFLICT(number) DO UPDATE SET name = excluded.name, chapter_count = excluded.chapter_count
                """, book);
        }
    }

    // A stage always has at least one display; also self-heals if the table was emptied.
    private void SeedDefaultDisplay()
    {
        using var connection = connectionFactory.Open();
        var count = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM displays");
        if (count > 0)
        {
            return;
        }
        connection.Execute(
            "INSERT INTO displays (name, sort_order, config_json) VALUES ('Display 1', 0, @ConfigJson)",
            new { ConfigJson = SqliteStageRepository.Serialize(Core.Domain.DisplayConfig.Default) });
    }

    private void SeedBundledTranslations()
    {
        using var connection = connectionFactory.Open();
        var existing = connection.Query<string>("SELECT code FROM translations").ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (code, name, language, license) in Bundled)
        {
            if (existing.Contains(code))
            {
                continue;
            }
            var seedFile = Path.Combine(options.Value.SeedDirectory, code + ".db");
            if (!File.Exists(seedFile))
            {
                logger.LogWarning("Seed file for {Code} not found at {Path}; skipping", code, seedFile);
                continue;
            }
            importer.Import(new ScrollmapperSource(code, name, language, license, seedFile));
        }
    }
}
