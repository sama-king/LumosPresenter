using System.Reflection;
using Dapper;
using Microsoft.Extensions.Logging;

namespace LumosPresenter.Data.Migrations;

/// <summary>
/// Applies embedded SQL migrations in version order at startup. Scripts are embedded
/// resources named "NNN_description.sql"; the leading number is the schema version.
/// </summary>
public sealed class MigrationRunner(SqliteConnectionFactory connectionFactory, ILogger<MigrationRunner> logger)
{
    public void Apply()
    {
        using var connection = connectionFactory.Open();
        connection.Execute("PRAGMA journal_mode = WAL;");
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version    INTEGER PRIMARY KEY,
                applied_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """);

        var applied = connection.Query<int>("SELECT version FROM schema_migrations").ToHashSet();

        foreach (var (version, name, sql) in LoadScripts())
        {
            if (applied.Contains(version))
            {
                continue;
            }
            using var transaction = connection.BeginTransaction();
            connection.Execute(sql, transaction: transaction);
            connection.Execute(
                "INSERT INTO schema_migrations (version) VALUES (@version)",
                new { version }, transaction);
            transaction.Commit();
            logger.LogInformation("Applied migration {Version} ({Name})", version, name);
        }
    }

    private static IEnumerable<(int Version, string Name, string Sql)> LoadScripts()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames()
            .Where(n => n.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            .Select(resource =>
            {
                var fileName = resource.Split('.')[^2]; // "...Migrations.001_initial.sql" → "001_initial"
                var version = int.Parse(fileName.Split('_')[0]);
                using var stream = assembly.GetManifestResourceStream(resource)!;
                using var reader = new StreamReader(stream);
                return (version, fileName, reader.ReadToEnd());
            })
            .OrderBy(script => script.version);
    }
}
