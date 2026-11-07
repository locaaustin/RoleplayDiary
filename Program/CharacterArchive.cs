namespace RoleplayDiary;

/// <summary>
/// One character, gathered up whole: their details, every entry in order, every
/// photo. The shape the PDF exporter wants, so it never has to go looking.
/// </summary>
public sealed class CharacterArchive
{
    public CharacterArchive(
        Character character,
        IEnumerable<DiaryEntry> entries,
        IEnumerable<CharacterPhoto> photos)
    {
        Character = character;
        Entries = entries.OrderBy(entry => entry.TimelineKey.Date)
                         .ThenBy(entry => entry.TimelineKey.Id)
                         .ToList();
        Photos = photos.ToList();
    }

    public Character Character { get; }

    /// <summary>Oldest first: beginning to end, which is how it gets exported.</summary>
    public IReadOnlyList<DiaryEntry> Entries { get; }

    public IReadOnlyList<CharacterPhoto> Photos { get; }

    /// <summary>The chosen cover, or the newest photo when nothing was chosen.</summary>
    public CharacterPhoto? Portrait
    {
        get
        {
            if (Character.PortraitPhotoId is { } chosen)
            {
                var match = Photos.FirstOrDefault(photo => photo.Id == chosen);
                if (match is not null)
                {
                    return match;
                }
            }

            return Photos.Count > 0 ? Photos[^1] : null;
        }
    }

    public string? PortraitPath => Portrait?.FullPath;

    /// <summary>The dated range the timeline covers, for the cover page.</summary>
    public string SpanLabel
    {
        get
        {
            var dated = Entries.Where(entry => entry.OccurredOn is not null)
                               .Select(entry => entry.OccurredOn!.Value)
                               .ToList();

            if (dated.Count == 0)
            {
                return string.Empty;
            }

            var first = dated.Min().Year;
            var last = dated.Max().Year;
            return first == last ? first.ToString() : $"{first} – {last}";
        }
    }

    public override string ToString() =>
        $"CharacterArchive({Character.Name}, {Entries.Count} entries, {Photos.Count} photos)";
}
