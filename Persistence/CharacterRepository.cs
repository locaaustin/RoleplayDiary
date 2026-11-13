using Microsoft.Data.Sqlite;
using RoleplayDiary.Domain;

namespace RoleplayDiary.Persistence;

/// <summary>The character roster.</summary>
public sealed class CharacterRepository : SqliteRepository
{
    public const string Columns =
        "Id, Name, Nickname, Age, Server, Occupation, Affiliation, " +
        "Status, Background, PortraitPhotoId, CreatedUtc, UpdatedUtc";

    public CharacterRepository(Database database) : base(database)
    {
    }

    protected override string Table => "Characters";

    public List<Character> GetAll() =>
        Query(
            $"SELECT {Columns} FROM {Table} ORDER BY Status, Name COLLATE NOCASE",
            Read);

    public Character? Get(long id) =>
        QueryOne(
            $"SELECT {Columns} FROM {Table} WHERE Id = $id",
            Read,
            ("$id", id));

    public Character Add(Character character)
    {
        character.MarkUpdated();
        character.Id = Insert(
            $"""
             INSERT INTO {Table}
                 (Name, Nickname, Age, Server, Occupation, Affiliation, Status,
                  Background, PortraitPhotoId, CreatedUtc, UpdatedUtc)
             VALUES
                 ($name, $nickname, $age, $server, $occupation, $affiliation, $status,
                  $background, $portrait, $created, $updated)
             """,
            Fields(character));

        return character;
    }

    public void Update(Character character)
    {
        character.MarkUpdated();
        Execute(
            $"""
             UPDATE {Table} SET
                 Name = $name, Nickname = $nickname, Age = $age, Server = $server,
                 Occupation = $occupation, Affiliation = $affiliation, Status = $status,
                 Background = $background, PortraitPhotoId = $portrait, UpdatedUtc = $updated
             WHERE Id = $id
             """,
            [..Fields(character), ("$id", character.Id)]);
    }

    public void SetStatus(long characterId, CharacterStatus status) =>
        Execute(
            $"UPDATE {Table} SET Status = $status, UpdatedUtc = $updated WHERE Id = $id",
            ("$status", (int)status),
            ("$updated", IsoFormat.ToText(DateTime.UtcNow)),
            ("$id", characterId));

    public void SetPortrait(long characterId, long? photoId) =>
        Execute(
            $"UPDATE {Table} SET PortraitPhotoId = $photo, UpdatedUtc = $updated WHERE Id = $id",
            ("$photo", photoId),
            ("$updated", IsoFormat.ToText(DateTime.UtcNow)),
            ("$id", characterId));

    private static (string Name, object? Value)[] Fields(Character character) =>
    [
        ("$name", character.Name),
        ("$nickname", character.Nickname),
        ("$age", character.Age),
        ("$server", character.Server),
        ("$occupation", character.Occupation),
        ("$affiliation", character.Affiliation),
        ("$status", (int)character.Status),
        ("$background", character.Background),
        ("$portrait", character.PortraitPhotoId),
        ("$created", IsoFormat.ToText(character.CreatedUtc)),
        ("$updated", IsoFormat.ToText(character.UpdatedUtc))
    ];

    private static Character Read(SqliteDataReader row) => new()
    {
        Id = row.Long("Id"),
        Name = row.RequiredText("Name"),
        Nickname = row.OptionalText("Nickname"),
        Age = row.OptionalInt("Age"),
        Server = row.OptionalText("Server"),
        Occupation = row.OptionalText("Occupation"),
        Affiliation = row.OptionalText("Affiliation"),
        Status = ChoiceLabels.Coerce<CharacterStatus>(row.Long("Status")),
        Background = row.OptionalText("Background"),
        PortraitPhotoId = row.OptionalLong("PortraitPhotoId"),
        CreatedUtc = IsoFormat.Parse(row.RequiredText("CreatedUtc")),
        UpdatedUtc = IsoFormat.Parse(row.RequiredText("UpdatedUtc"))
    };
}
