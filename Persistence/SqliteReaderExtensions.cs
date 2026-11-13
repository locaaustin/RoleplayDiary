using Microsoft.Data.Sqlite;

namespace RoleplayDiary.Persistence;

/// <summary>Named-column reads. SQLite returns DBNull for empty cells, and the typed getters throw on it.</summary>
internal static class SqliteReaderExtensions
{
    public static long Long(this SqliteDataReader row, string column) =>
        row.GetInt64(row.GetOrdinal(column));

    public static int Int(this SqliteDataReader row, string column) =>
        row.GetInt32(row.GetOrdinal(column));

    public static string RequiredText(this SqliteDataReader row, string column) =>
        row.GetString(row.GetOrdinal(column));

    public static string? OptionalText(this SqliteDataReader row, string column)
    {
        var ordinal = row.GetOrdinal(column);
        return row.IsDBNull(ordinal) ? null : row.GetString(ordinal);
    }

    public static int? OptionalInt(this SqliteDataReader row, string column)
    {
        var ordinal = row.GetOrdinal(column);
        return row.IsDBNull(ordinal) ? null : row.GetInt32(ordinal);
    }

    public static long? OptionalLong(this SqliteDataReader row, string column)
    {
        var ordinal = row.GetOrdinal(column);
        return row.IsDBNull(ordinal) ? null : row.GetInt64(ordinal);
    }
}
