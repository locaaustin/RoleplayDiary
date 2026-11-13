namespace RoleplayDiary.Domain;

/// <summary>Something that happened. The unit the story is told in.</summary>
public sealed class DiaryEntry : StoredEntity
{
    public long CharacterId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public EntryKind Kind { get; set; } = EntryKind.Journal;

    public DateOnly? OccurredOn { get; set; }

    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;

    public void MarkUpdated() => UpdatedUtc = DateTime.UtcNow;

    public string ShortDateLabel =>
        OccurredOn is { } date ? date.ToString("dd MMM yyyy") : "undated";

    public string KindLabel => Kind.Label();

    /// <summary>
    /// Oldest first. Undated entries use the day they were written, so they stay
    /// near the point in the story where the player added them.
    /// </summary>
    public (DateOnly Date, long Id) TimelineKey =>
        (OccurredOn ?? DateOnly.FromDateTime(CreatedUtc), Id);
}
