using Microsoft.Data.Sqlite;

namespace RoleplayDiary;

/// <summary>
/// Shared plumbing for the three tables: run a query, run a write, insert and get the
/// new id back, delete by id.
///
/// This is the inheritance in the project that actually earns its keep. Each of the
/// three repositories below says only what is different about its own table; every
/// line of parameter handling and reader looping lives here, once.
/// </summary>
public abstract class Repository
{
    protected Repository(Database database) => Db = database;

    protected Database Db { get; }

    /// <summary>The table this repository owns. Each subclass fills it in.</summary>
    protected abstract string Table { get; }

    /// <summary>
    /// Every value goes in as a parameter, never pasted into the SQL text. That is
    /// what makes a character called <c>'); DROP TABLE</c> merely an odd name.
    /// </summary>
    private static void Bind(SqliteCommand command, (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
    }

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

    /// <summary>Runs an INSERT and returns the id SQLite assigned to the new row.</summary>
    protected long Insert(string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Db.CreateCommand(sql + "; SELECT last_insert_rowid();");
        Bind(command, parameters);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public int Count()
    {
        using var command = Db.CreateCommand($"SELECT COUNT(*) FROM {Table}");
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// Virtual rather than plain, because deleting a photo has to do more than this
    /// and overrides it.
    /// </summary>
    public virtual void Delete(long id) =>
        Execute($"DELETE FROM {Table} WHERE Id = $id", ("$id", id));
}

/// <summary>The roster.</summary>
public sealed class CharacterRepository : Repository
{
    public CharacterRepository(Database database) : base(database)
    {
    }

    protected override string Table => "Characters";

    /// <summary>Living first, then deceased, then shelved; alphabetical inside each group.</summary>
    public List<Character> GetAll() =>
        Query(
            $"SELECT {Character.Columns} FROM {Table} ORDER BY Status, Name COLLATE NOCASE",
            Character.FromRow);

    public Character? Get(long id) =>
        QueryOne(
            $"SELECT {Character.Columns} FROM {Table} WHERE Id = $id",
            Character.FromRow,
            ("$id", id));

    /// <summary>Fills in the new id on the object it was given, then returns it.</summary>
    public Character Add(Character character)
    {
        character.Touch();
        character.Id = Insert(
            $"""
             INSERT INTO {Table}
                 (Name, Nickname, Age, Server, Occupation, Affiliation, Status,
                  Background, PortraitPhotoId, CreatedUtc, UpdatedUtc)
             VALUES
                 ($name, $nickname, $age, $server, $occupation, $affiliation, $status,
                  $background, $portrait, $created, $updated)
             """,
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
            ("$updated", IsoFormat.ToText(character.UpdatedUtc)));

        return character;
    }

    public void Update(Character character)
    {
        character.Touch();
        Execute(
            $"""
             UPDATE {Table} SET
                 Name = $name, Nickname = $nickname, Age = $age, Server = $server,
                 Occupation = $occupation, Affiliation = $affiliation, Status = $status,
                 Background = $background, PortraitPhotoId = $portrait, UpdatedUtc = $updated
             WHERE Id = $id
             """,
            ("$name", character.Name),
            ("$nickname", character.Nickname),
            ("$age", character.Age),
            ("$server", character.Server),
            ("$occupation", character.Occupation),
            ("$affiliation", character.Affiliation),
            ("$status", (int)character.Status),
            ("$background", character.Background),
            ("$portrait", character.PortraitPhotoId),
            ("$updated", IsoFormat.ToText(character.UpdatedUtc)),
            ("$id", character.Id));
    }

    public void SetStatus(long characterId, CharacterStatus status) =>
        Execute(
            $"UPDATE {Table} SET Status = $status, UpdatedUtc = $updated WHERE Id = $id",
            ("$status", (int)status),
            ("$updated", IsoFormat.ToText(IsoFormat.UtcNow)),
            ("$id", characterId));

    public void SetPortrait(long characterId, long? photoId) =>
        Execute(
            $"UPDATE {Table} SET PortraitPhotoId = $photo, UpdatedUtc = $updated WHERE Id = $id",
            ("$photo", photoId),
            ("$updated", IsoFormat.ToText(IsoFormat.UtcNow)),
            ("$id", characterId));
}

/// <summary>The timeline. Always read oldest-first, the way a story is told.</summary>
public sealed class DiaryEntryRepository : Repository
{
    public DiaryEntryRepository(Database database) : base(database)
    {
    }

    protected override string Table => "DiaryEntries";

    public List<DiaryEntry> GetForCharacter(long characterId) =>
        Query(
            $"""
             SELECT {DiaryEntry.Columns} FROM {Table}
             WHERE CharacterId = $character
             ORDER BY COALESCE(OccurredOn, CreatedUtc) ASC, Id ASC
             """,
            DiaryEntry.FromRow,
            ("$character", characterId));

    /// <summary>Every character's entry count in one query, for the roster.</summary>
    public Dictionary<long, int> EntryCounts()
    {
        var rows = Query(
            $"SELECT CharacterId, COUNT(*) FROM {Table} GROUP BY CharacterId",
            reader => (Id: reader.GetInt64(0), Total: reader.GetInt32(1)));

        return rows.ToDictionary(row => row.Id, row => row.Total);
    }

    public DiaryEntry Add(DiaryEntry entry)
    {
        entry.Touch();
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
        entry.Touch();
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
}

/// <summary>
/// The gallery. Rows hold only a file name; this is the one place that knows how to
/// turn that into a path on disk.
/// </summary>
public sealed class PhotoRepository : Repository
{
    private readonly AppPaths _paths;

    public PhotoRepository(Database database, AppPaths paths) : base(database) =>
        _paths = paths;

    protected override string Table => "Photos";

    private CharacterPhoto Build(SqliteDataReader row)
    {
        var photo = CharacterPhoto.FromRow(row);
        photo.FullPath = _paths.PhotoPath(photo.CharacterId, photo.FileName);
        return photo;
    }

    public List<CharacterPhoto> GetForCharacter(long characterId) =>
        Query(
            $"SELECT {CharacterPhoto.Columns} FROM {Table} WHERE CharacterId = $character ORDER BY Id",
            Build,
            ("$character", characterId));

    public Dictionary<long, List<CharacterPhoto>> GroupByCharacter()
    {
        var all = Query($"SELECT {CharacterPhoto.Columns} FROM {Table} ORDER BY Id", Build);

        var grouped = new Dictionary<long, List<CharacterPhoto>>();
        foreach (var photo in all)
        {
            if (!grouped.TryGetValue(photo.CharacterId, out var list))
            {
                grouped[photo.CharacterId] = list = new List<CharacterPhoto>();
            }

            list.Add(photo);
        }

        return grouped;
    }

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
        Execute($"UPDATE {Table} SET Caption = $caption WHERE Id = $id",
            ("$caption", caption), ("$id", photoId));

    /// <summary>
    /// Also clears the portrait pointer, which is a plain column with no foreign key
    /// to cascade for it.
    /// </summary>
    public override void Delete(long id)
    {
        Execute("UPDATE Characters SET PortraitPhotoId = NULL WHERE PortraitPhotoId = $id",
            ("$id", id));

        base.Delete(id);
    }
}
