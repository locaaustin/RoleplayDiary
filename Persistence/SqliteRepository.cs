using Microsoft.Data.Sqlite;

namespace RoleplayDiary.Persistence;

/// <summary>
/// Shared query and write helpers. Table names come from the subclass and are fixed
/// identifiers; every value is bound as a parameter.
/// </summary>
public abstract class SqliteRepository
{
    protected SqliteRepository(Database database) => Db = database;

    protected Database Db { get; }

    protected abstract string Table { get; }

    protected List<T> Query<T>(
        string sql,
        Func<SqliteDataReader, T> map,
        params (string Name, object? Value)[] parameters)
    {
        using var command = Db.CreateCommand(sql);
        Bind(command, parameters);

        using var reader = command.ExecuteReader();
        var results = new List<T>();
        while (reader.Read())
        {
            results.Add(map(reader));
        }

        return results;
    }

    protected T? QueryOne<T>(
        string sql,
        Func<SqliteDataReader, T> map,
        params (string Name, object? Value)[] parameters) where T : class
    {
        using var command = Db.CreateCommand(sql);
        Bind(command, parameters);

        using var reader = command.ExecuteReader();
        return reader.Read() ? map(reader) : null;
    }

    protected int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Db.CreateCommand(sql);
        Bind(command, parameters);
        return command.ExecuteNonQuery();
    }

    protected long Insert(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Db.CreateCommand(sql + "; SELECT last_insert_rowid();");
        Bind(command, parameters);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public virtual void Delete(long id) =>
        Execute($"DELETE FROM {Table} WHERE Id = $id", ("$id", id));

    private static void Bind(SqliteCommand command, (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }
}
