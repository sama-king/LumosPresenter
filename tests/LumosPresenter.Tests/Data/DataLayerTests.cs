using LumosPresenter.Core.Domain;
using LumosPresenter.Core.Parsing;
using LumosPresenter.Data;
using LumosPresenter.Data.Migrations;
using LumosPresenter.Data.Seeding;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Tests.Data;

public sealed class DataLayerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteConnectionFactory _factory;
    private readonly DatabaseInitializer _initializer;
    private readonly SqliteVerseRepository _repository;

    public DataLayerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "lumos-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "seed"));
        CreateFakeScrollmapperDb(Path.Combine(_tempDir, "seed", "KJV.db"), "KJV");

        var options = Options.Create(new DataOptions
        {
            DatabasePath = Path.Combine(_tempDir, "lumos.db"),
            SeedDirectory = Path.Combine(_tempDir, "seed"),
        });
        _factory = new SqliteConnectionFactory(options);
        var migrations = new MigrationRunner(_factory, NullLogger<MigrationRunner>.Instance);
        var importer = new ScrollmapperImporter(_factory, NullLogger<ScrollmapperImporter>.Instance);
        _initializer = new DatabaseInitializer(
            _factory, migrations, importer, options, NullLogger<DatabaseInitializer>.Instance);
        _repository = new SqliteVerseRepository(_factory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_tempDir, recursive: true);
    }

    /// <summary>Minimal file matching the scrollmapper shape: {CODE}_books + {CODE}_verses.</summary>
    private static void CreateFakeScrollmapperDb(string path, string code)
    {
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE {code}_books (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE {code}_verses (id INTEGER PRIMARY KEY, book_id INTEGER, chapter INTEGER, verse INTEGER, text TEXT);
            """;
        command.ExecuteNonQuery();

        using var insertBook = connection.CreateCommand();
        insertBook.CommandText = $"INSERT INTO {code}_books (id, name) VALUES ($id, $name)";
        var bookId = insertBook.Parameters.Add("$id", SqliteType.Integer);
        var bookName = insertBook.Parameters.Add("$name", SqliteType.Text);
        foreach (var book in BookCatalog.Books)
        {
            bookId.Value = book.Number;
            bookName.Value = book.Name;
            insertBook.ExecuteNonQuery();
        }

        using var insertVerse = connection.CreateCommand();
        insertVerse.CommandText =
            $"INSERT INTO {code}_verses (book_id, chapter, verse, text) VALUES ($b, $c, $v, $t)";
        void Add(int b, int c, int v, string t)
        {
            insertVerse.Parameters.Clear();
            insertVerse.Parameters.AddWithValue("$b", b);
            insertVerse.Parameters.AddWithValue("$c", c);
            insertVerse.Parameters.AddWithValue("$v", v);
            insertVerse.Parameters.AddWithValue("$t", t);
            insertVerse.ExecuteNonQuery();
        }
        Add(43, 3, 16, "For God so loved the world ");
        Add(43, 3, 17, "For God sent not his Son to condemn ");
        Add(43, 3, 18, "He that believeth is not condemned ");
        Add(1, 1, 1, "In the beginning ");
    }

    [Fact]
    public async Task Initialize_SeedsBooksTranslationsAndVerses()
    {
        _initializer.Initialize();

        var translations = await _repository.GetTranslationsAsync();
        Assert.Contains(translations, t => t.Id == "KJV");

        using var connection = _factory.Open();
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM books";
        Assert.Equal(66L, count.ExecuteScalar());
    }

    [Fact]
    public async Task GetVerses_SingleVerse()
    {
        _initializer.Initialize();
        var verses = await _repository.GetVersesAsync(
            "KJV", new BibleReference("John", 3, 16, null, 1.0));
        var verse = Assert.Single(verses);
        Assert.Equal("For God so loved the world", verse.Text); // importer trims
        Assert.Equal(16, verse.Number);
    }

    [Fact]
    public async Task GetVerses_Range()
    {
        _initializer.Initialize();
        var verses = await _repository.GetVersesAsync(
            "KJV", new BibleReference("John", 3, 16, 18, 1.0));
        Assert.Equal([16, 17, 18], verses.Select(v => v.Number));
    }

    [Fact]
    public async Task GetVerses_ChapterOnly_ReturnsWholeChapter()
    {
        _initializer.Initialize();
        var verses = await _repository.GetVersesAsync(
            "KJV", new BibleReference("John", 3, null, null, 1.0));
        Assert.Equal(3, verses.Count);
    }

    [Fact]
    public async Task GetVerses_UnknownTranslation_ReturnsEmpty()
    {
        _initializer.Initialize();
        Assert.Empty(await _repository.GetVersesAsync(
            "NIV", new BibleReference("John", 3, 16, null, 1.0)));
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        _initializer.Initialize();
        _initializer.Initialize(); // second run: migrations skip, seeding skips existing

        using var connection = _factory.Open();
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM translations WHERE code = 'KJV'";
        Assert.Equal(1L, count.ExecuteScalar());
    }
}
