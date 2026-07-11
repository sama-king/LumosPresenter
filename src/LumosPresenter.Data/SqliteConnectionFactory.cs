using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace LumosPresenter.Data;

/// <summary>Opens connections to the application database, creating the file on first use.</summary>
public sealed class SqliteConnectionFactory(IOptions<DataOptions> options)
{
    private readonly string _databasePath = options.Value.DatabasePath;

    public SqliteConnection Open()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_databasePath))!);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString());
        connection.Open();
        return connection;
    }
}
