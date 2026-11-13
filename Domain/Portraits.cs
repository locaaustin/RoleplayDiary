namespace RoleplayDiary.Domain;

/// <summary>Which photo stands in for a character: the chosen cover, otherwise the latest.</summary>
public static class Portraits
{
    public static CharacterPhoto? Choose(IReadOnlyList<CharacterPhoto>? photos, long? portraitPhotoId)
    {
        if (photos is null || photos.Count == 0)
        {
            return null;
        }

        var chosen = photos.FirstOrDefault(photo => photo.Id == portraitPhotoId);
        return chosen ?? photos[^1];
    }
}
