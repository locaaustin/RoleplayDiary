using Microsoft.Data.Sqlite;
using RoleplayDiary.Domain;

namespace RoleplayDiary.Persistence;

/// <summary>The timeline. Rows are read oldest-first, the way a story is told.</summary>
public sealed class DiaryEntryRepository : SqliteRepository
{
    public const string Columns =
        "Id, CharacterId, Title, Body, Kind, OccurredOn, CreatedUtc, UpdatedUtc";

    public DiaryEntryRepository(Database database) : base(database)
    {
    }

    protected override string Table => "DiaryEntries";

    public List<DiaryEntry> GetForCharacter(long characterId) =>
        Query(
            $"""
             SELECT {Columns} FROM {Table}
             WHERE CharacterId = $character
             ORDER BY COALESCE(OccurredOn, CreatedUtc) ASC, Id ASC
             """,
            Read,
            ("$character", characterId));

    public Dictionary<long, int> EntryCounts()
    {
        var rows = Query(
            $"SELECT CharacterId, COUNT(*) AS EntryCount FROM {Table} GROUP BY CharacterId",
            reader => (Id: reader.Long("CharacterId"), Total: reader.Int("EntryCount")));

        return rows.ToDictionary(row => row.Id, row => row.Total);
    }

    public DiaryEntry Add(DiaryEntry entry)
    {
        entry.MarkUpdated();
        entry.Id = Insert(
            $"""
             INSERT INTO {Table}
                 (CharacterId, Title, Body, Kind, OccurredOn, CreatedUtc, UpdatedUtc)
             VALUES
                 ($character, $title, $body, $kind, $occurred, $created, $updated)
             """,
            ("$character", entry.CharacterId),
            ("$title", entry.Title),
            ("$body", entry.Body),
            ("$kind", (int)entry.Kind),
            ("$occurred", IsoFormat.DateToText(entry.OccurredOn)),
            ("$created", IsoFormat.ToText(entry.CreatedUtc)),
            ("$updated", IsoFormat.ToText(entry.UpdatedUtc)));

        return entry;
    }

    public void Update(DiaryEntry entry)
    {
        entry.MarkUpdated();
        Execute(
            $"""
             UPDATE {Table} SET
                 Title = $title, Body = $body, Kind = $kind,
                 OccurredOn = $occurred, UpdatedUtc = $updated
             WHERE Id = $id
             """,
            ("$title", entry.Title),
            ("$body", entry.Body),
            ("$kind", (int)entry.Kind),
            ("$occurred", IsoFormat.DateToText(entry.OccurredOn)),
            ("$updated", IsoFormat.ToText(entry.UpdatedUtc)),
            ("$id", entry.Id));
    }

    private static DiaryEntry Read(SqliteDataReader row) => new()
    {
        Id = row.Long("Id"),
        CharacterId = row.Long("CharacterId"),
        Title = row.RequiredText("Title"),
        Body = row.OptionalText("Body"),
        Kind = ChoiceLabels.Coerce<EntryKind>(row.Long("Kind")),
        OccurredOn = IsoFormat.ParseDate(row.OptionalText("OccurredOn")),
        CreatedUtc = IsoFormat.Parse(row.RequiredText("CreatedUtc")),
        UpdatedUtc = IsoFormat.Parse(row.RequiredText("UpdatedUtc"))
    };
}
