#:package Microsoft.Data.Sqlite@10.0.9
// Pinned for the same reason as LumosPresenter.Data: Microsoft.Data.Sqlite still resolves
// SQLitePCLRaw 2.1.11 transitively, which carries a published advisory, and the repo builds
// with TreatWarningsAsErrors.
#:package SQLitePCLRaw.bundle_e_sqlite3@3.0.3

// Provisions the bundled translations (KJV, ASV, BSB) that DatabaseInitializer seeds on
// first run. Run from the repository root:
//
//     dotnet run scripts/fetch-seed-bibles.cs
//
// The seed files are gitignored and ~14 MB once imported, so they are fetched rather than
// committed. A .NET file-based program keeps this dependency-free: the SDK is already a
// prerequisite, so there is nothing to install on either macOS or Windows.
//
// Why this is not just a download:
//
// Upstream's SQLite exports (scrollmapper/bible_databases, formats/sqlite/*.db) currently
// contain SEVEN concatenated copies of each Bible — 217,714 verse rows for 31,102 verses,
// and 462 rows in {CODE}_books for 66 books. The copies are successive generation runs and
// they disagree: measured against upstream's own CSV export, the earliest copy differs in
// 152 verses (KJV), 938 (ASV) and 2,200 (BSB), while the last copy matches exactly. Feeding
// the file to ScrollmapperImporter as-is fails its 66-book integrity check.
//
// So: take the highest-id row per (book, chapter, verse) — the newest copy — then verify
// every verse against the CSV export, which is clean, before writing anything. A mismatch
// aborts rather than seeding text we have not checked.

using System.Text;
using Microsoft.Data.Sqlite;

const string Upstream = "https://raw.githubusercontent.com/scrollmapper/bible_databases/master/formats";

string[] codes = ["KJV", "ASV", "BSB"];
var seedDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
    "src", "LumosPresenter.WebHost", "data", "seed");
// Run from the repo root and the relative path is simply this:
if (Directory.Exists("src/LumosPresenter.WebHost"))
{
    seedDir = Path.Combine("src", "LumosPresenter.WebHost", "data", "seed");
}
seedDir = Path.GetFullPath(seedDir);
Directory.CreateDirectory(seedDir);

var force = args.Contains("--force");
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
var temp = Directory.CreateTempSubdirectory("lumos-seed-");

try
{
    foreach (var code in codes)
    {
        var target = Path.Combine(seedDir, code + ".db");
        if (File.Exists(target) && !force)
        {
            Console.WriteLine($"{code}: already present at {target} (use --force to refetch)");
            continue;
        }

        var rawDb = Path.Combine(temp.FullName, code + ".upstream.db");
        var rawCsv = Path.Combine(temp.FullName, code + ".csv");
        await Download($"{Upstream}/sqlite/{code}.db", rawDb);
        await Download($"{Upstream}/csv/{code}.csv", rawCsv);

        var oracle = ReadCsv(rawCsv);
        var (books, verses) = ReadNewestCopy(rawDb, code);
        Verify(code, books, verses, oracle);
        Write(target, code, books, verses);
        Console.WriteLine($"{code}: wrote {verses.Count:N0} verses, {books.Count} books -> {target}");
    }
}
finally
{
    temp.Delete(recursive: true);
}

async Task Download(string url, string path)
{
    Console.WriteLine($"  fetching {url}");
    await using var stream = await http.GetStreamAsync(url);
    await using var file = File.Create(path);
    await stream.CopyToAsync(file);
}

// Upstream duplicates every verse; the highest id for a given (book, chapter, verse) is the
// most recent generation run. Book ids beyond 66 belong to the stale copies.
static (Dictionary<int, string> Books, List<(int Book, int Chapter, int Verse, string Text)> Verses)
    ReadNewestCopy(string path, string code)
{
    using var connection = new SqliteConnection(
        new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
    connection.Open();

    var books = new Dictionary<int, string>();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = $"SELECT id, name FROM {code}_books WHERE id <= 66 ORDER BY id";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            books[reader.GetInt32(0)] = reader.GetString(1).Trim();
        }
    }

    var verses = new List<(int, int, int, string)>();
    using (var command = connection.CreateCommand())
    {
        command.CommandText = $"""
            SELECT book_id, chapter, verse, text FROM {code}_verses
            WHERE id IN (SELECT MAX(id) FROM {code}_verses GROUP BY book_id, chapter, verse)
            ORDER BY book_id, chapter, verse
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            verses.Add((reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3).Trim()));
        }
    }
    return (books, verses);
}

static Dictionary<(string Book, int Chapter, int Verse), string> ReadCsv(string path)
{
    var rows = new Dictionary<(string, int, int), string>();
    var first = true;
    foreach (var fields in ParseCsv(path))
    {
        if (first) { first = false; continue; }
        if (fields.Length < 4) continue;
        rows[(fields[0], int.Parse(fields[1]), int.Parse(fields[2]))] = fields[3].Trim();
    }
    return rows;
}

// RFC 4180: verse text carries commas and quoted passages, so a split(',') will not do.
static IEnumerable<string[]> ParseCsv(string path)
{
    using var reader = new StreamReader(path, Encoding.UTF8);
    var field = new StringBuilder();
    var row = new List<string>();
    var quoted = false;
    int next;
    while ((next = reader.Read()) >= 0)
    {
        var c = (char)next;
        if (quoted)
        {
            if (c != '"') { field.Append(c); }
            else if (reader.Peek() == '"') { reader.Read(); field.Append('"'); }
            else { quoted = false; }
        }
        else if (c == '"') { quoted = true; }
        else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
        else if (c == '\n') { row.Add(field.ToString()); field.Clear(); yield return [.. row]; row.Clear(); }
        else if (c != '\r') { field.Append(c); }
    }
    if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); yield return [.. row]; }
}

static void Verify(
    string code,
    Dictionary<int, string> books,
    List<(int Book, int Chapter, int Verse, string Text)> verses,
    Dictionary<(string, int, int), string> oracle)
{
    if (books.Count != 66)
    {
        throw new InvalidDataException($"{code}: expected 66 books after de-duplication, got {books.Count}.");
    }
    if (verses.Count != oracle.Count)
    {
        throw new InvalidDataException(
            $"{code}: de-duplicated to {verses.Count} verses but the CSV export has {oracle.Count}.");
    }
    var mismatches = 0;
    foreach (var (book, chapter, verse, text) in verses)
    {
        if (!oracle.TryGetValue((books[book], chapter, verse), out var expected) || expected != text)
        {
            mismatches++;
        }
    }
    if (mismatches > 0)
    {
        throw new InvalidDataException(
            $"{code}: {mismatches:N0} verses disagree with the CSV export; refusing to write an unverified seed.");
    }
    Console.WriteLine($"  verified {verses.Count:N0} verses against the CSV export");
}

// Emits the shape ScrollmapperImporter reads: {CODE}_books (66 rows, canonical order) and
// {CODE}_verses referencing them.
static void Write(
    string target,
    string code,
    Dictionary<int, string> books,
    List<(int Book, int Chapter, int Verse, string Text)> verses)
{
    if (File.Exists(target)) { File.Delete(target); }
    using var connection = new SqliteConnection(
        new SqliteConnectionStringBuilder { DataSource = target, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false }.ToString());
    connection.Open();

    using (var command = connection.CreateCommand())
    {
        command.CommandText = $"""
            CREATE TABLE {code}_books (id INTEGER PRIMARY KEY, name TEXT);
            CREATE TABLE {code}_verses (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                book_id INTEGER, chapter INTEGER, verse INTEGER, text TEXT,
                FOREIGN KEY (book_id) REFERENCES {code}_books(id)
            );
            """;
        command.ExecuteNonQuery();
    }

    using var transaction = connection.BeginTransaction();
    using (var insert = connection.CreateCommand())
    {
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT INTO {code}_books (id, name) VALUES ($id, $name)";
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var name = insert.Parameters.Add("$name", SqliteType.Text);
        foreach (var (bookId, bookName) in books.OrderBy(b => b.Key))
        {
            id.Value = bookId;
            name.Value = bookName;
            insert.ExecuteNonQuery();
        }
    }
    using (var insert = connection.CreateCommand())
    {
        insert.Transaction = transaction;
        insert.CommandText =
            $"INSERT INTO {code}_verses (book_id, chapter, verse, text) VALUES ($b, $c, $v, $t)";
        var b = insert.Parameters.Add("$b", SqliteType.Integer);
        var c = insert.Parameters.Add("$c", SqliteType.Integer);
        var v = insert.Parameters.Add("$v", SqliteType.Integer);
        var t = insert.Parameters.Add("$t", SqliteType.Text);
        foreach (var (book, chapter, verse, text) in verses)
        {
            b.Value = book; c.Value = chapter; v.Value = verse; t.Value = text;
            insert.ExecuteNonQuery();
        }
    }
    transaction.Commit();
}
