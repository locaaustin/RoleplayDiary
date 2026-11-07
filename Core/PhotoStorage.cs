namespace RoleplayDiary;

/// <summary>
/// Photos are <i>copied in</i>, never referenced in place: the player can clean out
/// their screenshots folder a year later and the archive still holds together.
///
/// Imported files get a generated name, so two screenshots both called image.png
/// never collide.
/// </summary>
public sealed class PhotoStorage
{
    public static readonly IReadOnlySet<string> SupportedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
        };

    private readonly AppPaths _paths;
    private readonly PhotoRepository _photos;

    public PhotoStorage(AppPaths paths, PhotoRepository photos)
    {
        _paths = paths;
        _photos = photos;
    }

    public static bool CanRead(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>The extension list in the shape the Windows file dialog wants.</summary>
    public static string FileDialogFilter()
    {
        var patterns = string.Join(";", SupportedExtensions.Order().Select(ext => "*" + ext));
        return $"Images ({patterns})|{patterns}|All files (*.*)|*.*";
    }

    /// <summary>
    /// Copy one file in and record it. Returns null for anything unreadable, so one
    /// bad file in a multi-select never aborts the rest.
    /// </summary>
    public CharacterPhoto? Import(long characterId, string source)
    {
        if (!CanRead(source) || !File.Exists(source))
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

    /// <summary>
    /// Import several, skipping any the app cannot read. The order of the result
    /// matches the order given, so the caller can treat the first as the cover.
    /// </summary>
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

    /// <summary>Delete the row and the file. A file already gone is not an error.</summary>
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
            // The row is gone, which is what matters; a locked file can wait.
        }
    }

    /// <summary>Used when a whole character is deleted.</summary>
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
