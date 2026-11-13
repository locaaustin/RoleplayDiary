using RoleplayDiary.Domain;
using RoleplayDiary.Persistence;

namespace RoleplayDiary.Services;

/// <summary>
/// The only object the rest of the app uses to read and change a diary.
/// View models never see a repository, a connection, or a file path.
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

    public void Dispose() => _database.Dispose();

    public List<RosterItem> Roster()
    {
        var counts = _entries.EntryCounts();
        var photosByCharacter = _photos.GroupByCharacter();

        return _characters.GetAll()
            .Select(character =>
            {
                photosByCharacter.TryGetValue(character.Id, out var photos);
                counts.TryGetValue(character.Id, out var count);
                return new RosterItem(character, count, PortraitPath(character, photos));
            })
            .ToList();
    }

    public Character? GetCharacter(long id) => _characters.Get(id);

    public string? PortraitPath(Character character) =>
        PortraitPath(character, _photos.GetForCharacter(character.Id));

    public Character CreateCharacter(CharacterDraft draft)
    {
        var character = new Character();
        draft.ApplyTo(character);
        _characters.Add(character);
        AdoptCoverIfMissing(character, _storage.ImportMany(character.Id, draft.Photos));
        return character;
    }

    /// <summary>Returns how many staged photos could not be read.</summary>
    public int UpdateCharacter(Character character, CharacterDraft draft)
    {
        draft.ApplyTo(character);
        _characters.Update(character);

        var imported = _storage.ImportMany(character.Id, draft.Photos);
        AdoptCoverIfMissing(character, imported);
        return draft.Photos.Count - imported.Count;
    }

    public void SetStatus(Character character, CharacterStatus status)
    {
        character.Status = status;
        _characters.SetStatus(character.Id, status);
    }

    public void SetBackground(Character character, string? text)
    {
        character.Background = StoredText.OrNull(text);
        _characters.Update(character);
    }

    public void DeleteCharacter(long characterId)
    {
        _storage.RemoveAllFor(characterId);
        _characters.Delete(characterId);
    }

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

    public List<CharacterPhoto> Photos(long characterId) => _photos.GetForCharacter(characterId);

    /// <summary>Returns how many files were skipped because they could not be read.</summary>
    public int AddPhotos(Character character, IReadOnlyList<string> sources)
    {
        var imported = _storage.ImportMany(character.Id, sources);
        AdoptCoverIfMissing(character, imported);
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
        photo.Caption = StoredText.OrNull(caption);
        _photos.SetCaption(photo.Id, photo.Caption);
    }

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

    private static string? PortraitPath(Character character, IReadOnlyList<CharacterPhoto>? photos) =>
        Portraits.Choose(photos, character.PortraitPhotoId)?.FullPath;

    private void AdoptCoverIfMissing(Character character, IReadOnlyList<CharacterPhoto> imported)
    {
        if (imported.Count == 0 || character.PortraitPhotoId is not null)
        {
            return;
        }

        character.PortraitPhotoId = imported[0].Id;
        _characters.SetPortrait(character.Id, character.PortraitPhotoId);
    }
}
