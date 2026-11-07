using Microsoft.Data.Sqlite;

namespace RoleplayDiary;

/// <summary>
/// Reading columns that are allowed to be empty.
///
/// SQLite hands back DBNull rather than null, and the typed getters throw on it, so
/// every nullable column has to be checked first. These three keep that check in one
/// place instead of scattering it through every mapping.
/// </summary>
public static class SqliteReaderExtensions
{
    public static string? NullableString(this SqliteDataReader row, int column) =>
        row.IsDBNull(column) ? null : row.GetString(column);

    public static int? NullableInt(this SqliteDataReader row, int column) =>
        row.IsDBNull(column) ? null : row.GetInt32(column);

    public static long? NullableLong(this SqliteDataReader row, int column) =>
        row.IsDBNull(column) ? null : row.GetInt64(column);
}

/// <summary>
/// Owns the one connection the app uses and brings the schema up to date on open.
///
/// Schema changes are append-only: add a script to <see cref="Migrations"/> and
/// SQLite's own <c>user_version</c> pragma records how far an install has got.
/// Index 0 is migration 1. <b>Never edit a script that has shipped</b> - existing
/// diaries have already run it.
/// </summary>
public sealed class Database : IDisposable
{
    /// <summary>
    /// The schema, one script per version. This is the same schema the Python and C#
    /// versions before it created, which is why an existing diary opens unchanged.
    /// </summary>
    private static readonly string[] Migrations =
    {
        // 1 - the original schema.
        """
        CREATE TABLE Characters (
            Id              INTEGER PRIMARY KEY AUTOINCREMENT,
            Name            TEXT    NOT NULL,
            Nickname        TEXT    NULL,
            Age             INTEGER NULL,
            Server          TEXT    NULL,
            Occupation      TEXT    NULL,
            Affiliation     TEXT    NULL,
            Status          INTEGER NOT NULL DEFAULT 0,
            Background      TEXT    NULL,
            PortraitPhotoId INTEGER NULL,
            CreatedUtc      TEXT    NOT NULL,
            UpdatedUtc      TEXT    NOT NULL
        );

        CREATE TABLE DiaryEntries (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            CharacterId INTEGER NOT NULL REFERENCES Characters(Id) ON DELETE CASCADE,
            Title       TEXT    NOT NULL,
            Body        TEXT    NULL,
            Kind        INTEGER NOT NULL DEFAULT 0,
            OccurredOn  TEXT    NULL,
            CreatedUtc  TEXT    NOT NULL,
            UpdatedUtc  TEXT    NOT NULL
        );

        CREATE TABLE Photos (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            CharacterId INTEGER NOT NULL REFERENCES Characters(Id) ON DELETE CASCADE,
            FileName    TEXT    NOT NULL,
            Caption     TEXT    NULL,
            TakenOn     TEXT    NULL,
            CreatedUtc  TEXT    NOT NULL
        );

        CREATE INDEX IX_DiaryEntries_CharacterId ON DiaryEntries(CharacterId);
        CREATE INDEX IX_Photos_CharacterId ON Photos(CharacterId);
        """
    };

    private readonly AppPaths _paths;
    private SqliteConnection? _connection;

    public Database(AppPaths paths) => _paths = paths;

    /// <summary>Connect, enforce foreign keys, then migrate. Safe to call twice.</summary>
    public SqliteConnection Open()
    {
        if (_connection is not null)
        {
            return _connection;
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.Database,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        _connection = new SqliteConnection(builder.ToString());
        _connection.Open();

        Execute("PRAGMA foreign_keys = ON");
        Execute("PRAGMA journal_mode = WAL");

        Migrate();
        return _connection;
    }

    public SqliteConnection Connection =>
        _connection ?? throw new InvalidOperationException("Database.Open() must be called first.");

    /// <summary>
    /// A command with the connection already attached. The caller disposes it, which
    /// is what the <c>using</c> at every call site is doing.
    /// </summary>
    public SqliteCommand CreateCommand(string sql)
    {
        var command = Connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }

    public int Execute(string sql)
    {
        using var command = CreateCommand(sql);
        return command.ExecuteNonQuery();
    }

    public int SchemaVersion
    {
        get
        {
            using var command = CreateCommand("PRAGMA user_version");
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    private void Migrate()
    {
        var current = SchemaVersion;

        for (var version = 1; version <= Migrations.Length; version++)
        {
            if (version <= current)
            {
                continue;
            }

            using var transaction = Connection.BeginTransaction();
            using (var command = Connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = Migrations[version - 1];
                command.ExecuteNonQuery();

                // Pragmas take no parameters, hence the interpolation; version is an int.
                command.CommandText = $"PRAGMA user_version = {version}";
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    /// <summary>
    /// Implementing IDisposable is what lets callers write <c>using var db = ...</c>
    /// and have the connection closed for them however the block is left.
    /// </summary>
    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }

    public override string ToString() =>
        $"Database({AppPaths.DatabaseName}, {(_connection is null ? "closed" : "open")})";
}
