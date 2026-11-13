using Microsoft.Data.Sqlite;

namespace RoleplayDiary.Persistence;

/// <summary>
/// Opens the one connection the app uses and applies schema changes on startup.
/// Migrations are append-only. Never edit a script that has already shipped:
/// existing diaries have already run it. Index 0 is migration 1.
/// </summary>
public sealed class Database : IDisposable
{
    private static readonly string[] Migrations =
    {
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

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }

    private int SchemaVersion
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

                command.CommandText = $"PRAGMA user_version = {version}";
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }
}
