using Microsoft.Data.Sqlite;
using RoleplayDiary.Domain;

namespace RoleplayDiary.Persistence;

/// <summary>The gallery. Rows store a file name; this type turns that into a path.</summary>
public sealed class PhotoRepository : SqliteRepository
{
    public const string Columns = "Id, CharacterId, FileName, Caption, TakenOn, CreatedUtc";

    private readonly AppPaths _paths;

    public PhotoRepository(Database database, AppPaths paths) : base(database) =>
        _paths = paths;

    protected override string Table => "Photos";

    public List<CharacterPhoto> GetForCharacter(long characterId) =>
        Query(
            $"SELECT {Columns} FROM {Table} WHERE CharacterId = $character ORDER BY Id",
            Read,
            ("$character", characterId));

    public Dictionary<long, List<CharacterPhoto>> GroupByCharacter() =>
        Query($"SELECT {Columns} FROM {Table} ORDER BY Id", Read)
            .GroupBy(photo => photo.CharacterId)
            .ToDictionary(group => group.Key, group => group.ToList());

    public CharacterPhoto Add(CharacterPhoto photo)
    {
        photo.Id = Insert(
            $"""
             INSERT INTO {Table} (CharacterId, FileName, Caption, TakenOn, CreatedUtc)
             VALUES ($character, $file, $caption, $taken, $created)
             """,
            ("$character", photo.CharacterId),
            ("$file", photo.FileName),
            ("$caption", photo.Caption),
            ("$taken", IsoFormat.DateToText(photo.TakenOn)),
            ("$created", IsoFormat.ToText(photo.CreatedUtc)));

        photo.FullPath = _paths.PhotoPath(photo.CharacterId, photo.FileName);
        return photo;
    }

    public void SetCaption(long photoId, string? caption) =>
        Execute(
            $"UPDATE {Table} SET Caption = $caption WHERE Id = $id",
            ("$caption", caption),
            ("$id", photoId));

    /// <summary>
    /// PortraitPhotoId has no foreign key, so deleting a photo also clears any cover that pointed at it.
    /// </summary>
    public override void Delete(long id)
    {
        Execute(
            "UPDATE Characters SET PortraitPhotoId = NULL WHERE PortraitPhotoId = $id",
            ("$id", id));

        base.Delete(id);
    }

    private CharacterPhoto Read(SqliteDataReader row)
    {
        var photo = new CharacterPhoto
        {
            Id = row.Long("Id"),
            CharacterId = row.Long("CharacterId"),
            FileName = row.RequiredText("FileName"),
            Caption = row.OptionalText("Caption"),
            TakenOn = IsoFormat.ParseDate(row.OptionalText("TakenOn")),
            CreatedUtc = IsoFormat.Parse(row.RequiredText("CreatedUtc"))
        };

        photo.FullPath = _paths.PhotoPath(photo.CharacterId, photo.FileName);
        return photo;
    }
}
