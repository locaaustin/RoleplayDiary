namespace RoleplayDiary;

/// <summary>
/// Everything the rest of the app is allowed to ask for, behind one object.
///
/// Nothing above this line sees a repository, a connection or a file path. The view
/// models ask the diary for characters and tell it what the player did; the diary
/// decides what that means for the database and the disk.
/// </summary>
public sealed class Diary : IDisposable
{
    private readonly Database _database;
    private readonly CharacterRepository _characters;
    private readonly DiaryEntryRepository _entries;
    private readonly PhotoRepository _photos;
    private readonly PhotoStorage _storage;
    private readonly PdfExporter _pdf = new();

    public Diary(string? root = null)
    {
        Paths = new AppPaths(root);
        _database = new Database(Paths);
        _database.Open();

        _characters = new CharacterRepository(_database);
        _entries = new DiaryEntryRepository(_database);
        _photos = new PhotoRepository(_database, Paths);
        _storage = new PhotoStorage(Paths, _photos);
    }

    public AppPaths Paths { get; }

    public int SchemaVersion => _database.SchemaVersion;

    public void Dispose() => _database.Dispose();

    // ------------------------------------------------------------- the roster

    /// <summary>Every character, with their entry count and cover photo resolved.</summary>
    public List<RosterItem> Roster()
    {
        var counts = _entries.EntryCounts();
        var grouped = _photos.GroupByCharacter();

        return _characters.GetAll()
            .Select(character =>
            {
                grouped.TryGetValue(character.Id, out var photos);
                counts.TryGetValue(character.Id, out var count);
                return new RosterItem(character, count, PickPortrait(photos, character));
            })
            .ToList();
    }

    private static string? PickPortrait(List<CharacterPhoto>? photos, Character character)
    {
        if (photos is null || photos.Count == 0)
        {
            return null;
        }

        var chosen = photos.FirstOrDefault(photo => photo.Id == character.PortraitPhotoId);
        return (chosen ?? photos[^1]).FullPath;
    }

    public Character? GetCharacter(long id) => _characters.Get(id);

    public string? PortraitPath(Character character) =>
        PickPortrait(_photos.GetForCharacter(character.Id), character);

    // --------------------------------------------------------- the characters

    /// <summary>
    /// Make a character, then import any photos staged while creating them. The
    /// photos could only wait until now, because filing them needs an id.
    /// </summary>
    public Character CreateCharacter(CharacterDraft draft)
    {
        var character = new Character();
        draft.ApplyTo(character);
        _characters.Add(character);

        var imported = _storage.ImportMany(character.Id, draft.Photos);
        if (imported.Count > 0)
        {
            character.PortraitPhotoId = imported[0].Id;
            _characters.SetPortrait(character.Id, character.PortraitPhotoId);
        }

        return character;
    }

    /// <summary>Returns how many of the staged photos could not be read.</summary>
    public int UpdateCharacter(Character character, CharacterDraft draft)
    {
        draft.ApplyTo(character);
        _characters.Update(character);

        var imported = _storage.ImportMany(character.Id, draft.Photos);
        if (imported.Count > 0 && character.PortraitPhotoId is null)
        {
            // A character with no cover yet adopts the first photo added here.
            character.PortraitPhotoId = imported[0].Id;
            _characters.SetPortrait(character.Id, character.PortraitPhotoId);
        }

        return draft.Photos.Count - imported.Count;
    }

    public void SetStatus(Character character, CharacterStatus status)
    {
        character.Status = status;
        _characters.SetStatus(character.Id, status);
    }

    public void SetBackground(Character character, string? text)
    {
        character.Background = CharacterDraft.Blank(text);
        _characters.Update(character);
    }

    /// <summary>Photos first: the files on disk have no foreign key to cascade for them.</summary>
    public void DeleteCharacter(long characterId)
    {
        _storage.RemoveAllFor(characterId);
        _characters.Delete(characterId);
    }

    // -------------------------------------------------------------- the story

    public List<DiaryEntry> Entries(long characterId) => _entries.GetForCharacter(characterId);

    public DiaryEntry AddEntry(long characterId, EntryDraft draft)
    {
        var entry = new DiaryEntry { CharacterId = characterId };
        draft.ApplyTo(entry);
        return _entries.Add(entry);
    }

    public void UpdateEntry(DiaryEntry entry, EntryDraft draft)
    {
        draft.ApplyTo(entry);
        _entries.Update(entry);
    }

    public void DeleteEntry(long entryId) => _entries.Delete(entryId);

    // ------------------------------------------------------------ the gallery

    public List<CharacterPhoto> Photos(long characterId) => _photos.GetForCharacter(characterId);

    /// <summary>Returns how many files were skipped because they could not be read.</summary>
    public int AddPhotos(Character character, IReadOnlyList<string> sources)
    {
        var imported = _storage.ImportMany(character.Id, sources);

        if (imported.Count > 0 && character.PortraitPhotoId is null)
        {
            character.PortraitPhotoId = imported[0].Id;
            _characters.SetPortrait(character.Id, character.PortraitPhotoId);
        }

        return sources.Count - imported.Count;
    }

    public void SetCover(Character character, CharacterPhoto photo)
    {
        character.PortraitPhotoId = photo.Id;
        _characters.SetPortrait(character.Id, photo.Id);
    }

    public void RemovePhoto(Character character, CharacterPhoto photo)
    {
        _storage.Remove(photo);

        if (character.PortraitPhotoId == photo.Id)
        {
            character.PortraitPhotoId = null;
        }
    }

    public void SetCaption(CharacterPhoto photo, string? caption)
    {
        photo.Caption = CharacterDraft.Blank(caption);
        _photos.SetCaption(photo.Id, photo.Caption);
    }

    // ------------------------------------------------------------- the export

    public CharacterArchive? Archive(long characterId)
    {
        var character = _characters.Get(characterId);
        return character is null
            ? null
            : new CharacterArchive(character, Entries(characterId), Photos(characterId));
    }

    public static string SuggestedPdfName(Character character) =>
        PdfExporter.SuggestedFileName(character.Name);

    public string? ExportPdf(long characterId, string destination) =>
        Archive(characterId) is { } archive ? _pdf.Export(archive, destination) : null;
}
