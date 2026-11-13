using RoleplayDiary.Domain;

namespace RoleplayDiary.Persistence;

/// <summary>
/// Copies photos into the diary instead of remembering where they came from.
/// Stored files get a generated name, so two screenshots both called image.png never collide.
/// One unreadable file does not abort the rest of a multi-select.
/// </summary>
public sealed class PhotoStorage
{
    private readonly AppPaths _paths;
    private readonly PhotoRepository _photos;

    public PhotoStorage(AppPaths paths, PhotoRepository photos)
    {
        _paths = paths;
        _photos = photos;
    }

    public CharacterPhoto? Import(long characterId, string source)
    {
        if (!ImageFiles.HasSupportedExtension(source) || !File.Exists(source))
        {
            return null;
        }

        var storedName = Guid.NewGuid().ToString("N") + Path.GetExtension(source).ToLowerInvariant();
        var destination = Path.Combine(_paths.MediaFor(characterId), storedName);

        try
        {
            File.Copy(source, destination, overwrite: false);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return _photos.Add(new CharacterPhoto
        {
            CharacterId = characterId,
            FileName = storedName
        });
    }

    public List<CharacterPhoto> ImportMany(long characterId, IEnumerable<string> sources)
    {
        var imported = new List<CharacterPhoto>();

        foreach (var source in sources)
        {
            if (Import(characterId, source) is { } photo)
            {
                imported.Add(photo);
            }
        }

        return imported;
    }

    public void Remove(CharacterPhoto photo)
    {
        _photos.Delete(photo.Id);

        if (photo.FullPath is null)
        {
            return;
        }

        try
        {
            File.Delete(photo.FullPath);
        }
        catch (IOException)
        {
            // The row is already gone. A locked file can be cleaned up later.
        }
    }

    public void RemoveAllFor(long characterId)
    {
        foreach (var photo in _photos.GetForCharacter(characterId))
        {
            Remove(photo);
        }

        var folder = Path.Combine(_paths.Media, characterId.ToString());
        if (!Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
