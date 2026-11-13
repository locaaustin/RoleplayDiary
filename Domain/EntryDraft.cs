namespace RoleplayDiary.Domain;

/// <summary>Values collected by the entry editor before they become a saved <see cref="DiaryEntry"/>.</summary>
public sealed class EntryDraft
{
    public string Title { get; set; } = string.Empty;

    public EntryKind Kind { get; set; } = EntryKind.Journal;

    public string? WhenText { get; set; }

    public string? Body { get; set; }

    public bool IsValid => !string.IsNullOrWhiteSpace(Title);

    public static EntryDraft From(DiaryEntry entry) => new()
    {
        Title = entry.Title,
        Kind = entry.Kind,
        WhenText = entry.OccurredOn?.ToString("yyyy-MM-dd"),
        Body = entry.Body
    };

    public void ApplyTo(DiaryEntry entry)
    {
        entry.Title = Title.Trim();
        entry.Kind = Kind;
        entry.OccurredOn = FlexibleDate.Parse(WhenText);
        entry.Body = StoredText.OrNull(Body);
    }
}
