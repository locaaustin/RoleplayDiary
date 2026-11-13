namespace RoleplayDiary.Domain;

/// <summary>
/// One character gathered whole: details, timeline, and photos. Built for export
/// so the exporter does not query the database itself.
/// </summary>
public sealed class CharacterArchive
{
    public CharacterArchive(
        Character character,
        IEnumerable<DiaryEntry> entries,
        IEnumerable<CharacterPhoto> photos)
    {
        Character = character;
        Entries = entries
            .OrderBy(entry => entry.TimelineKey.Date)
            .ThenBy(entry => entry.TimelineKey.Id)
            .ToList();
        Photos = photos.ToList();
    }

    public Character Character { get; }

    public IReadOnlyList<DiaryEntry> Entries { get; }

    public IReadOnlyList<CharacterPhoto> Photos { get; }

    public CharacterPhoto? Portrait => Portraits.Choose(Photos, Character.PortraitPhotoId);

    public string? PortraitPath => Portrait?.FullPath;

    /// <summary>The years the dated timeline covers, for the cover page.</summary>
    public string SpanLabel
    {
        get
        {
            var dated = Entries
                .Where(entry => entry.OccurredOn is not null)
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
}
